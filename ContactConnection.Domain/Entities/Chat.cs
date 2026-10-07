namespace ContactConnection.Domain.Entities;

// ── Team chat (S183) ─────────────────────────────────────────────────────────────────────────────────────────────────
// Tenant-scoped, Slack-like chat for agents, supervisors and admins: channels (configured by chat managers), direct
// messages (1:1 and group), threads, reactions and @mentions. Channels carry three independent controls (Stephen):
//   • posting restricted — only selected people may post (chat managers always can);
//   • membership locked  — people assigned (individually or by role) can't leave;
//   • retired            — history stays readable, nobody posts until it is un-retired.

public static class ChatChannelKind
{
    public const string Channel = "channel";
    public const string Direct  = "dm";
}

public class ChatChannel
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Kind { get; private set; } = ChatChannelKind.Channel;

    public string Name { get; private set; } = string.Empty;          // channels only; DMs are named by their members
    public string? Description { get; private set; }
    /// <summary>Private channels are visible only to their members; public ones can be browsed and joined.</summary>
    public bool IsPrivate { get; private set; }

    /// <summary>Only <see cref="PosterIds"/>, holders of <see cref="PosterRoleIds"/> (and chat managers) may post.</summary>
    public bool PostingRestricted { get; private set; }
    public List<Guid> PosterIds { get; private set; } = [];
    /// <summary>Everyone holding one of these custom roles may post — follows role changes without editing the channel.</summary>
    public List<Guid> PosterRoleIds { get; private set; } = [];

    /// <summary>Who may pin messages for everyone in this channel (chat managers always can; in a DM, any member).</summary>
    public List<Guid> PinnerIds { get; private set; } = [];
    public List<Guid> PinnerRoleIds { get; private set; } = [];

    /// <summary>Who may delete other people's messages in this channel (chat managers always can; everyone can delete their own).</summary>
    public List<Guid> ModeratorIds { get; private set; } = [];
    public List<Guid> ModeratorRoleIds { get; private set; } = [];

    /// <summary>Assigned members — individually (<see cref="ChatMember.IsAssigned"/>) or through
    /// <see cref="AssignedRoleIds"/> — can't leave.</summary>
    public bool MembershipLocked { get; private set; }
    /// <summary>Everyone holding one of these custom roles is a member automatically.</summary>
    public List<Guid> AssignedRoleIds { get; private set; } = [];

    public DateTimeOffset? RetiredAt { get; private set; }
    public bool IsRetired => RetiredAt is not null;

    /// <summary>DMs only: the members' ids, sorted and joined — one conversation per set of people.</summary>
    public string? DirectKey { get; private set; }

    public Guid? CreatedById { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>Last top-level message or reply — orders the channel list.</summary>
    public DateTimeOffset? LastMessageAt { get; private set; }

    private ChatChannel() { }

    public static ChatChannel CreateChannel(Guid tenantId, string name, string? description, bool isPrivate, Guid? createdById)
    {
        var now = DateTimeOffset.UtcNow;
        var c = new ChatChannel
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Kind = ChatChannelKind.Channel,
            IsPrivate = isPrivate, CreatedById = createdById, CreatedAt = now, UpdatedAt = now,
        };
        c.Rename(name, description);
        return c;
    }

    public static ChatChannel CreateDirect(Guid tenantId, IEnumerable<Guid> memberIds, Guid createdById)
    {
        var now = DateTimeOffset.UtcNow;
        return new ChatChannel
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Kind = ChatChannelKind.Direct, IsPrivate = true,
            DirectKey = DirectKeyFor(memberIds), CreatedById = createdById, CreatedAt = now, UpdatedAt = now,
        };
    }

    public static string DirectKeyFor(IEnumerable<Guid> memberIds) =>
        string.Join(",", memberIds.Distinct().Select(i => i.ToString("N")).Order(StringComparer.Ordinal));

    public bool IsDirect => Kind == ChatChannelKind.Direct;

    public void Rename(string name, string? description)
    {
        var n = (name ?? "").Trim().TrimStart('#').Trim();
        if (n.Length is 0 or > 80) throw new ArgumentException("A channel name is 1–80 characters.", nameof(name));
        Name = n;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()[..Math.Min(description.Trim().Length, 300)];
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Configure(bool isPrivate, bool postingRestricted, IEnumerable<Guid> posterIds, bool membershipLocked, IEnumerable<Guid> assignedRoleIds,
        IEnumerable<Guid>? posterRoleIds = null)
    {
        IsPrivate = isPrivate;
        PostingRestricted = postingRestricted;
        PosterIds = posterIds.Distinct().ToList();
        PosterRoleIds = (posterRoleIds ?? []).Distinct().ToList();
        MembershipLocked = membershipLocked;
        AssignedRoleIds = assignedRoleIds.Distinct().ToList();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetPinners(IEnumerable<Guid> pinnerIds, IEnumerable<Guid> pinnerRoleIds)
    {
        PinnerIds = pinnerIds.Distinct().ToList();
        PinnerRoleIds = pinnerRoleIds.Distinct().ToList();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetModerators(IEnumerable<Guid> moderatorIds, IEnumerable<Guid> moderatorRoleIds)
    {
        ModeratorIds = moderatorIds.Distinct().ToList();
        ModeratorRoleIds = moderatorRoleIds.Distinct().ToList();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>May this person delete someone else's message here? Chat managers anywhere; moderators in a live channel.</summary>
    public bool CanDeleteOthers(Guid agentId, Guid? roleId, bool isChatManager) =>
        isChatManager || (!IsDirect && !IsRetired && (ModeratorIds.Contains(agentId) || (roleId is { } r && ModeratorRoleIds.Contains(r))));

    /// <summary>May this person pin / unpin messages for everyone here?</summary>
    public bool CanPinForEveryone(Guid agentId, Guid? roleId, bool isChatManager) =>
        !IsRetired && (IsDirect || isChatManager || PinnerIds.Contains(agentId) || (roleId is { } r && PinnerRoleIds.Contains(r)));

    public void Retire()   { RetiredAt ??= DateTimeOffset.UtcNow; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Unretire() { RetiredAt = null; UpdatedAt = DateTimeOffset.UtcNow; }

    public void Touch(DateTimeOffset at) { LastMessageAt = at; }

    /// <summary>Why this person can't post here, or null. Membership is checked by the caller.</summary>
    /// <param name="roleId">The person's current custom role, if any.</param>
    public string? PostBlockedReason(Guid agentId, Guid? roleId, bool isChatManager)
    {
        if (IsRetired) return "This channel is retired — its history is read-only.";
        if (PostingRestricted && !isChatManager && !PosterIds.Contains(agentId) && !(roleId is { } r && PosterRoleIds.Contains(r)))
            return "Only selected people can post in this channel.";
        return null;
    }
}

public class ChatMember
{
    public Guid ChannelId { get; private set; }
    public Guid AgentId { get; private set; }
    /// <summary>Put here by a chat manager (or by role) rather than joining themselves — can't leave a membership-locked channel.</summary>
    public bool IsAssigned { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    /// <summary>Everything up to here has been read.</summary>
    public DateTimeOffset LastReadAt { get; private set; }
    /// <summary>Left the channel (kept so a role assignment doesn't silently re-add them).</summary>
    public DateTimeOffset? LeftAt { get; private set; }
    public bool IsActive => LeftAt is null;

    private ChatMember() { }

    public static ChatMember Create(Guid channelId, Guid agentId, bool assigned)
    {
        var now = DateTimeOffset.UtcNow;
        return new ChatMember { ChannelId = channelId, AgentId = agentId, IsAssigned = assigned, JoinedAt = now, LastReadAt = now };
    }

    public void Rejoin(bool assigned)
    {
        if (LeftAt is not null) { LeftAt = null; JoinedAt = DateTimeOffset.UtcNow; LastReadAt = JoinedAt; }
        IsAssigned |= assigned;
    }
    public void SetAssigned(bool assigned) => IsAssigned = assigned;
    public void Leave() => LeftAt = DateTimeOffset.UtcNow;
    public void MarkRead(DateTimeOffset at) { if (at > LastReadAt) LastReadAt = at; }
}

public static class ChatMessageKind
{
    public const string User   = "user";
    public const string System = "system";
}

/// <summary>How a message body is written (S183): plain text with &lt;@id&gt; mention tokens, or sanitized HTML from the
/// rich composer (formatting, pasted images as data-chat-file references, mentions as data-mention spans).</summary>
public static class ChatMessageFormat
{
    public const string Text = "text";
    public const string Html = "html";
}

public class ChatMessage
{
    public Guid Id { get; private set; }
    public Guid ChannelId { get; private set; }
    /// <summary>Null for system messages.</summary>
    public Guid? AgentId { get; private set; }
    public string Kind { get; private set; } = ChatMessageKind.User;
    /// <summary>Thread replies point at their top-level message.</summary>
    public Guid? ParentId { get; private set; }
    /// <summary>Text (mentions as &lt;@agentId&gt; tokens) or sanitized HTML — see <see cref="Format"/>.</summary>
    public string Body { get; private set; } = string.Empty;
    public string Format { get; private set; } = ChatMessageFormat.Text;
    /// <summary>The words alone — search, notifications, pinned previews.</summary>
    public string BodyText { get; private set; } = string.Empty;
    public List<Guid> MentionIds { get; private set; } = [];
    /// <summary>@channel — everyone in the channel is notified.</summary>
    public bool MentionsChannel { get; private set; }
    /// <summary>Files attached to the message (not images, which sit in the body) — ids for queries, details for display.</summary>
    public List<Guid> AttachmentIds { get; private set; } = [];
    public List<ChatAttachment> Attachments { get; private set; } = [];
    public int ReplyCount { get; private set; }
    public DateTimeOffset? LastReplyAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    /// <summary>Pinned for everyone in the channel (S183) — shown highlighted, and at the top of the conversation.</summary>
    public DateTimeOffset? PinnedAt { get; private set; }
    public Guid? PinnedById { get; private set; }

    public const int MaxLength = 4000;
    /// <summary>Formatted messages carry markup; the words are still capped at <see cref="MaxLength"/>.</summary>
    public const int MaxHtmlLength = 40000;

    private ChatMessage() { }

    public static ChatMessage Create(Guid channelId, Guid? agentId, string body, Guid? parentId, string kind = ChatMessageKind.User)
    {
        var text = (body ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException("A message can't be empty.", nameof(body));
        if (text.Length > MaxLength) throw new ArgumentException($"A message is at most {MaxLength} characters.", nameof(body));
        return new ChatMessage
        {
            Id = Guid.NewGuid(), ChannelId = channelId, AgentId = agentId, Kind = kind, ParentId = parentId,
            Body = text, BodyText = text, MentionIds = ParseMentions(text), CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>A formatted message. <paramref name="html"/> must already be sanitized; <paramref name="text"/> is its words.</summary>
    public const int MaxAttachments = 10;

    public static ChatMessage CreateRich(Guid channelId, Guid agentId, string html, string text, bool hasImages, Guid? parentId,
        IReadOnlyList<ChatAttachment>? attachments = null)
    {
        var m = new ChatMessage
        {
            Id = Guid.NewGuid(), ChannelId = channelId, AgentId = agentId, Kind = ChatMessageKind.User, ParentId = parentId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (attachments is { Count: > 0 })
        {
            if (attachments.Count > MaxAttachments) throw new ArgumentException($"At most {MaxAttachments} files per message.");
            m.Attachments = attachments.ToList();
            m.AttachmentIds = attachments.Select(a => a.Id).Distinct().ToList();
        }
        m.SetRich(html, text, hasImages);
        return m;
    }

    private void SetRich(string html, string text, bool hasImages)
    {
        var words = (text ?? "").Trim();
        if (words.Length == 0 && !hasImages && Attachments.Count == 0) throw new ArgumentException("A message can't be empty.", nameof(text));
        if (words.Length > MaxLength) throw new ArgumentException($"A message is at most {MaxLength} characters.", nameof(text));
        if ((html ?? "").Length > MaxHtmlLength) throw new ArgumentException("That message has too much formatting — try splitting it up.", nameof(html));
        Format = ChatMessageFormat.Html;
        Body = html!;
        BodyText = words.Length > 0 ? words : hasImages ? "[image]" : "[file]";
        MentionIds = ParseHtmlMentions(html!);
        MentionsChannel = html!.Contains("data-mention=\"channel\"", StringComparison.Ordinal);
    }

    public void Edit(string body)
    {
        var text = (body ?? "").Trim();
        if (text.Length is 0 or > MaxLength) throw new ArgumentException($"A message is 1–{MaxLength} characters.", nameof(body));
        Format = ChatMessageFormat.Text;
        Body = text;
        BodyText = text;
        MentionIds = ParseMentions(text);
        EditedAt = DateTimeOffset.UtcNow;
    }

    public void EditRich(string html, string text, bool hasImages)
    {
        SetRich(html, text, hasImages);
        EditedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Mentions in formatted messages: data-mention="guid" attributes, in order, once each.</summary>
    public static List<Guid> ParseHtmlMentions(string html)
    {
        var ids = new List<Guid>();
        const string attr = "data-mention=\"";
        var i = 0;
        while ((i = html.IndexOf(attr, i, StringComparison.Ordinal)) >= 0)
        {
            var start = i + attr.Length;
            var end = html.IndexOf('"', start);
            if (end < 0) break;
            if (Guid.TryParse(html.AsSpan(start, end - start), out var id) && !ids.Contains(id)) ids.Add(id);
            i = end + 1;
        }
        return ids;
    }

    /// <summary>Clears the content; its images and files are then unreferenced and the cleanup job removes them.</summary>
    public void Delete()
    {
        DeletedAt = DateTimeOffset.UtcNow; Body = string.Empty; BodyText = string.Empty; MentionIds = []; MentionsChannel = false;
        Attachments = []; AttachmentIds = [];
        Unpin();
    }

    public void Pin(Guid byId)
    {
        if (DeletedAt is not null) throw new InvalidOperationException("A deleted message can't be pinned.");
        PinnedAt ??= DateTimeOffset.UtcNow;
        PinnedById ??= byId;
    }
    public void Unpin() { PinnedAt = null; PinnedById = null; }

    public void AddReply(DateTimeOffset at) { ReplyCount++; LastReplyAt = at; }
    public void RemoveReply() { if (ReplyCount > 0) ReplyCount--; }

    /// <summary>The ids in &lt;@guid&gt; tokens, in order, once each.</summary>
    public static List<Guid> ParseMentions(string text)
    {
        var ids = new List<Guid>();
        var i = 0;
        while ((i = text.IndexOf("<@", i, StringComparison.Ordinal)) >= 0)
        {
            var end = text.IndexOf('>', i);
            if (end < 0) break;
            if (Guid.TryParse(text.AsSpan(i + 2, end - i - 2), out var id) && !ids.Contains(id)) ids.Add(id);
            i = end + 1;
        }
        return ids;
    }
}

public class ChatReaction
{
    public Guid MessageId { get; private set; }
    public Guid AgentId { get; private set; }
    public string Emoji { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private ChatReaction() { }

    public static ChatReaction Create(Guid messageId, Guid agentId, string emoji)
    {
        var e = (emoji ?? "").Trim();
        if (e.Length is 0 or > 32) throw new ArgumentException("Pick an emoji.", nameof(emoji));   // ZWJ / skin-tone sequences run long
        return new ChatReaction { MessageId = messageId, AgentId = agentId, Emoji = e, CreatedAt = DateTimeOffset.UtcNow };
    }
}

/// <summary>An image pasted or attached in chat (S183) — stored in blob storage, served only to signed-in people who can see a
/// message that uses it (or its uploader).</summary>
public class ChatFile
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentId { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>image (shown inline) or file (an attachment, always downloaded).</summary>
    public string Kind { get; private set; } = ChatFileKind.Image;
    public string FileName { get; private set; } = string.Empty;

    public const long MaxBytes = 5 * 1024 * 1024;
    public const long MaxFileBytes = 25 * 1024 * 1024;
    /// <summary>Unsent / no-longer-used uploads are removed after this.</summary>
    public static readonly TimeSpan OrphanGrace = TimeSpan.FromHours(24);

    /// <summary>Programs and scripts are never shared through chat.</summary>
    public static readonly IReadOnlySet<string> BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msp", ".bat", ".cmd", ".com", ".scr", ".pif", ".cpl", ".dll", ".sys", ".ps1", ".psm1", ".vbs", ".vbe",
        ".js", ".jse", ".wsf", ".wsh", ".hta", ".lnk", ".jar", ".reg", ".app", ".sh", ".appx", ".msix", ".iso", ".img", ".vhd",
    };

    private ChatFile() { }
    public static ChatFile Create(Guid tenantId, Guid agentId, string contentType, long size, string kind = ChatFileKind.Image, string? fileName = null)
    {
        var id = Guid.NewGuid();
        return new ChatFile
        {
            Id = id, TenantId = tenantId, AgentId = agentId, ContentType = contentType, SizeBytes = size, Kind = kind,
            FileName = CleanName(fileName), StorageKey = $"chat/{tenantId:N}/{id:N}", CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>A safe display / download name: no path, no control characters, at most 150 characters.</summary>
    public static string CleanName(string? name)
    {
        var n = Path.GetFileName((name ?? "").Replace('\\', '/').Split('/').Last()).Trim();
        n = new string(n.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?')).ToArray());
        if (n.Length > 150) n = n[..100] + "…" + n[^40..];
        return n.Length == 0 ? "file" : n;
    }

    /// <summary>The image type from the file's own first bytes — never trust the declared type (no SVG / HTML).</summary>
    public static string? SniffImageType(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return "image/png";
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "image/jpeg";
        if (head.Length >= 6 && head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x38) return "image/gif";
        if (head.Length >= 12 && head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46
            && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50) return "image/webp";
        return null;
    }
}

public static class ChatFileKind
{
    public const string Image = "image";
    public const string File  = "file";
}

/// <summary>A file attached to a message, as shown on its card.</summary>
public class ChatAttachment
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

/// <summary>A message someone pinned for themselves only (S183).</summary>
public class ChatPersonalPin
{
    public Guid AgentId { get; private set; }
    public Guid MessageId { get; private set; }
    public Guid ChannelId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ChatPersonalPin() { }
    public static ChatPersonalPin Create(Guid agentId, Guid messageId, Guid channelId) =>
        new() { AgentId = agentId, MessageId = messageId, ChannelId = channelId, CreatedAt = DateTimeOffset.UtcNow };
}

/// <summary>An agent's assigned supervisor (S183) — who an agent's "raise hand" reaches first.</summary>
public class AgentSupervisor
{
    public Guid AgentId { get; private set; }
    public Guid SupervisorId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AgentSupervisor() { }
    public static AgentSupervisor Create(Guid agentId, Guid supervisorId)
    {
        if (agentId == supervisorId) throw new ArgumentException("Someone can't be their own supervisor.");
        return new AgentSupervisor { AgentId = agentId, SupervisorId = supervisorId, CreatedAt = DateTimeOffset.UtcNow };
    }
}

public static class HelpRequestStatus
{
    public const string Open      = "open";
    public const string Claimed   = "claimed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// An agent's raised hand (S183): every on-duty supervisor assigned to them is alerted (all on-duty supervisors when none
/// of theirs is); the first to pick it up gets a direct message with the agent.
/// </summary>
public class HelpRequest
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentId { get; private set; }
    public string Status { get; private set; } = HelpRequestStatus.Open;
    public string? Note { get; private set; }
    /// <summary>The call the agent was on, if any — shown to the supervisor (and their Monitor tools).</summary>
    public Guid? CallRecordId { get; private set; }
    /// <summary>Who was alerted.</summary>
    public List<Guid> NotifiedIds { get; private set; } = [];
    /// <summary>True when none of the agent's own supervisors was on duty and every on-duty supervisor was alerted.</summary>
    public bool WentToAllSupervisors { get; private set; }
    public Guid? ClaimedById { get; private set; }
    public Guid? ChannelId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    private HelpRequest() { }

    public static HelpRequest Create(Guid tenantId, Guid agentId, string? note, Guid? callRecordId, IEnumerable<Guid> notifiedIds, bool wentToAll) =>
        new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AgentId = agentId, CallRecordId = callRecordId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 300)],
            NotifiedIds = notifiedIds.Distinct().ToList(), WentToAllSupervisors = wentToAll, CreatedAt = DateTimeOffset.UtcNow,
        };

    public bool IsOpen => Status == HelpRequestStatus.Open;

    public void Claim(Guid supervisorId, Guid channelId)
    {
        if (!IsOpen) throw new InvalidOperationException("This help request has already been picked up or cancelled.");
        Status = HelpRequestStatus.Claimed; ClaimedById = supervisorId; ChannelId = channelId; ClosedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        if (!IsOpen) return;
        Status = HelpRequestStatus.Cancelled; ClosedAt = DateTimeOffset.UtcNow;
    }
}
