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

public class ChatMessage
{
    public Guid Id { get; private set; }
    public Guid ChannelId { get; private set; }
    /// <summary>Null for system messages.</summary>
    public Guid? AgentId { get; private set; }
    public string Kind { get; private set; } = ChatMessageKind.User;
    /// <summary>Thread replies point at their top-level message.</summary>
    public Guid? ParentId { get; private set; }
    /// <summary>Text; mentions are written as &lt;@agentId&gt; tokens and rendered as names.</summary>
    public string Body { get; private set; } = string.Empty;
    public List<Guid> MentionIds { get; private set; } = [];
    public int ReplyCount { get; private set; }
    public DateTimeOffset? LastReplyAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public const int MaxLength = 4000;

    private ChatMessage() { }

    public static ChatMessage Create(Guid channelId, Guid? agentId, string body, Guid? parentId, string kind = ChatMessageKind.User)
    {
        var text = (body ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException("A message can't be empty.", nameof(body));
        if (text.Length > MaxLength) throw new ArgumentException($"A message is at most {MaxLength} characters.", nameof(body));
        return new ChatMessage
        {
            Id = Guid.NewGuid(), ChannelId = channelId, AgentId = agentId, Kind = kind, ParentId = parentId,
            Body = text, MentionIds = ParseMentions(text), CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Edit(string body)
    {
        var text = (body ?? "").Trim();
        if (text.Length is 0 or > MaxLength) throw new ArgumentException($"A message is 1–{MaxLength} characters.", nameof(body));
        Body = text;
        MentionIds = ParseMentions(text);
        EditedAt = DateTimeOffset.UtcNow;
    }

    public void Delete() { DeletedAt = DateTimeOffset.UtcNow; Body = string.Empty; MentionIds = []; }

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
        if (e.Length is 0 or > 16) throw new ArgumentException("Pick an emoji.", nameof(emoji));
        return new ChatReaction { MessageId = messageId, AgentId = agentId, Emoji = e, CreatedAt = DateTimeOffset.UtcNow };
    }
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
