using System.Text.Json;
using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Team chat (S183). Everyone signed in to the tenant (agents, supervisors, admins) chats in channels and direct
/// messages, sees everyone's real status, and an agent can raise a hand to their assigned supervisors. Channels are
/// created and configured only on the chat admin page (permission chat.manage). Gated by the tenant's TenantChat switch.
/// Every change is pushed over /hubs/chat to exactly the people it concerns.
/// </summary>
public static class ChatEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string NotEnabled = "Team chat isn't enabled for this account — ContactConnection support can switch it on.";

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/chat").RequireAuthorization();
        g.MapGet("bootstrap", Bootstrap);
        g.MapGet("channels/browse", Browse);
        g.MapPost("channels/{id:guid}/join", Join);
        g.MapPost("channels/{id:guid}/leave", Leave);
        g.MapGet("channels/{id:guid}/messages", Messages);
        g.MapGet("messages/{id:guid}/thread", Thread);
        g.MapPost("channels/{id:guid}/messages", Post);
        g.MapPatch("messages/{id:guid}", Edit);
        g.MapDelete("messages/{id:guid}", Delete);
        g.MapPost("messages/{id:guid}/reactions", React);
        g.MapGet("channels/{id:guid}/pins", Pins);
        g.MapPost("messages/{id:guid}/pin", (Guid id, PinRequest req, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
            IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct) => SetPin(id, req.Scope, true, http, tc, dbf, hub, ct));
        g.MapDelete("messages/{id:guid}/pin", (Guid id, string? scope, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
            IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct) => SetPin(id, scope, false, http, tc, dbf, hub, ct));
        g.MapPost("channels/{id:guid}/read", MarkRead);
        g.MapPost("channels/{id:guid}/typing", Typing);
        g.MapPost("direct", OpenDirect);
        g.MapGet("search", Search);
        g.MapPost("files", UploadFile).DisableAntiforgery();
        g.MapGet("files/{id:guid}", GetFile);
        g.MapPost("help", RaiseHand);
        g.MapPost("help/{id:guid}/claim", ClaimHelp);
        g.MapPost("help/{id:guid}/cancel", CancelHelp);

        var admin = app.MapGroup("/api/v1/chat/admin").RequireAuthorization("ChatManage");
        admin.MapGet("channels", AdminList);
        // People + roles for the pickers — a chat manager may not hold the agents / roles permissions.
        admin.MapGet("directory", async (ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct) =>
        {
            if (!tc.HasTenant) return Results.Unauthorized();
            await using var db = dbf.Create();
            var people = await db.Agents.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.FirstName).ThenBy(a => a.LastName)
                .Select(a => new { a.Id, name = (a.FirstName + " " + a.LastName).Trim(), a.Email, a.RoleId }).ToListAsync(ct);
            var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).Select(r => new { r.Id, r.Name }).ToListAsync(ct);
            return Results.Ok(new { people, roles });
        });
        admin.MapPost("channels", AdminCreate);
        admin.MapPut("channels/{id:guid}", AdminUpdate);
        admin.MapPost("channels/{id:guid}/retire", (Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct) => SetRetired(id, true, dbf, tc, hub, ct));
        admin.MapPost("channels/{id:guid}/unretire", (Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct) => SetRetired(id, false, dbf, tc, hub, ct));

        // Assigned supervisors live on the agent (Agents page).
        app.MapGet("/api/v1/admin/agents/{id:guid}/supervisors", GetSupervisors).RequireAuthorization("AgentsView");
        app.MapGet("/api/v1/admin/agent-supervisors", async (ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct) =>
        {
            if (!tc.HasTenant) return Results.Unauthorized();
            await using var db = dbf.Create();
            return Results.Ok(await db.AgentSupervisors.AsNoTracking().Select(s => new { s.AgentId, s.SupervisorId }).ToListAsync(ct));
        }).RequireAuthorization("AgentsView");
        app.MapPut("/api/v1/admin/agents/{id:guid}/supervisors", SetSupervisors).RequireAuthorization("TenantAdmin");
        return app;
    }

    // ── Who's asking ─────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Me(Guid Id, Guid TenantId, bool IsManager, bool IsSupervisor);

    private static Me? WhoAmI(HttpContext http, TenantContext tc)
    {
        if (!tc.HasTenant || !Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id)) return null;
        var perms = (http.User.FindFirst("permissions")?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        return new Me(id, tc.Current!.Id, perms.Contains(Permission.ChatManage), perms.Contains(Permission.SupervisorMonitor));
    }

    private static bool Enabled(TenantContext tc) => tc.Current?.FeatureFlags.TenantChat == true;

    private static Task Push(IHubContext<ChatHub, IChatHubClient> hub, IEnumerable<Guid> agentIds, string type, object payload)
    {
        var groups = agentIds.Distinct().Select(ChatHub.UserGroup).ToList();
        return groups.Count == 0 ? Task.CompletedTask
            : hub.Clients.Groups(groups).ReceiveChatEvent(type, JsonSerializer.Serialize(payload, Json));
    }

    private static Task<List<Guid>> ActiveMemberIds(TenantDbContext db, Guid channelId, CancellationToken ct) =>
        db.ChatMembers.Where(m => m.ChannelId == channelId && m.LeftAt == null).Select(m => m.AgentId).ToListAsync(ct);

    private static Task<ChatMember?> Membership(TenantDbContext db, Guid channelId, Guid agentId, CancellationToken ct) =>
        db.ChatMembers.FirstOrDefaultAsync(m => m.ChannelId == channelId && m.AgentId == agentId && m.LeftAt == null, ct);

    /// <summary>Role-assigned channels: make sure this person is a member (a locked one re-adds someone who left).</summary>
    private static async Task EnsureRoleMembershipsAsync(TenantDbContext db, Guid agentId, CancellationToken ct)
    {
        var roleId = await db.Agents.Where(a => a.Id == agentId).Select(a => a.RoleId).FirstOrDefaultAsync(ct);
        if (roleId is null) return;
        var channels = await db.ChatChannels.Where(c => c.Kind == ChatChannelKind.Channel && c.AssignedRoleIds.Contains(roleId.Value))
            .Select(c => new { c.Id, c.MembershipLocked }).ToListAsync(ct);
        if (channels.Count == 0) return;
        var ids = channels.Select(c => c.Id).ToList();
        var rows = await db.ChatMembers.Where(m => m.AgentId == agentId && ids.Contains(m.ChannelId)).ToListAsync(ct);
        foreach (var c in channels)
        {
            var row = rows.FirstOrDefault(r => r.ChannelId == c.Id);
            if (row is null) db.ChatMembers.Add(ChatMember.Create(c.Id, agentId, assigned: true));
            else if (row.IsActive) row.SetAssigned(true);
            else if (c.MembershipLocked) row.Rejoin(assigned: true);
        }
        await db.SaveChangesAsync(ct);
    }

    // ── Shapes ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static object ChannelDto(ChatChannel c, ChatMember m, Me me, Guid? roleId, List<Guid> members, int unread, int mentions) => new
    {
        c.Id, c.Kind, c.Name, c.Description, c.IsPrivate, c.PostingRestricted, c.PosterIds, c.PosterRoleIds, c.MembershipLocked,
        c.PinnerIds, c.PinnerRoleIds, canPin = c.CanPinForEveryone(me.Id, roleId, me.IsManager),
        c.ModeratorIds, c.ModeratorRoleIds, canDeleteAny = c.CanDeleteOthers(me.Id, roleId, me.IsManager),
        retired = c.IsRetired, c.LastMessageAt, lastReadAt = m.LastReadAt,
        memberIds = members,
        canPost = c.PostBlockedReason(me.Id, roleId, me.IsManager) is null,
        postBlockedReason = c.PostBlockedReason(me.Id, roleId, me.IsManager),
        canLeave = !c.IsDirect && !(c.MembershipLocked && (m.IsAssigned || (roleId is { } r && c.AssignedRoleIds.Contains(r)))),
        unread, mentions,
    };

    private static async Task<object?> ChannelForAsync(TenantDbContext db, Guid channelId, Me me, CancellationToken ct)
    {
        var c = await db.ChatChannels.FirstOrDefaultAsync(x => x.Id == channelId, ct);
        var m = c is null ? null : await Membership(db, c.Id, me.Id, ct);
        if (c is null || m is null) return null;
        var members = await ActiveMemberIds(db, c.Id, ct);
        var counts = await UnreadAsync(db, [(c.Id, m.LastReadAt)], me.Id, ct);
        var roleId = await db.Agents.Where(a => a.Id == me.Id).Select(a => a.RoleId).FirstOrDefaultAsync(ct);
        return ChannelDto(c, m, me, roleId, members, counts.GetValueOrDefault(c.Id).Unread, counts.GetValueOrDefault(c.Id).Mentions);
    }

    private static async Task<Dictionary<Guid, (int Unread, int Mentions)>> UnreadAsync(
        TenantDbContext db, List<(Guid ChannelId, DateTimeOffset ReadAt)> reads, Guid me, CancellationToken ct)
    {
        if (reads.Count == 0) return [];
        var ids = reads.Select(r => r.ChannelId).ToList();
        var oldest = reads.Min(r => r.ReadAt);
        var recent = await db.ChatMessages.AsNoTracking()
            .Where(m => ids.Contains(m.ChannelId) && m.CreatedAt > oldest && m.DeletedAt == null && (m.AgentId == null || m.AgentId != me))
            .Select(m => new { m.ChannelId, m.CreatedAt, m.ParentId, Mentioned = m.MentionIds.Contains(me) })
            .ToListAsync(ct);
        var readAt = reads.ToDictionary(r => r.ChannelId, r => r.ReadAt);
        return recent.Where(m => m.CreatedAt > readAt[m.ChannelId]).GroupBy(m => m.ChannelId)
            .ToDictionary(g => g.Key, g => (g.Count(m => m.ParentId == null), g.Count(m => m.Mentioned)));
    }

    private static object MessageDto(ChatMessage m, IEnumerable<ChatReaction> reactions) => new
    {
        m.Id, m.ChannelId, m.AgentId, m.Kind, m.ParentId,
        body = m.DeletedAt is null ? m.Body : "", m.Format, bodyText = m.DeletedAt is null ? m.BodyText : "", m.MentionIds, m.ReplyCount, m.LastReplyAt, m.CreatedAt, m.EditedAt,
        deleted = m.DeletedAt is not null,
        m.PinnedAt, m.PinnedById,
        reactions = reactions.GroupBy(r => r.Emoji).OrderBy(g => g.Min(r => r.CreatedAt))
            .Select(g => new { emoji = g.Key, agentIds = g.Select(r => r.AgentId).ToList() }),
    };

    private static async Task<List<object>> WithReactionsAsync(TenantDbContext db, List<ChatMessage> messages, CancellationToken ct)
    {
        var ids = messages.Select(m => m.Id).ToList();
        var reactions = await db.ChatReactions.AsNoTracking().Where(r => ids.Contains(r.MessageId)).ToListAsync(ct);
        var by = reactions.ToLookup(r => r.MessageId);
        return messages.Select(m => MessageDto(m, by[m.Id])).ToList();
    }

    private static async Task<object> HelpDto(TenantDbContext db, HelpRequest h, CancellationToken ct)
    {
        var names = await db.Agents.AsNoTracking().Where(a => a.Id == h.AgentId || a.Id == h.ClaimedById)
            .ToDictionaryAsync(a => a.Id, a => (a.FirstName + " " + a.LastName).Trim(), ct);
        var call = h.CallRecordId is { } cid
            ? await db.CallRecords.AsNoTracking().Where(r => r.Id == cid)
                .Select(r => new { r.CallerId, Campaign = db.Campaigns.Where(c => c.Id == r.CampaignId).Select(c => c.Name).FirstOrDefault() })
                .FirstOrDefaultAsync(ct)
            : null;
        return new
        {
            h.Id, h.AgentId, agentName = names.GetValueOrDefault(h.AgentId), h.Status, h.Note, h.CallRecordId,
            callerNumber = call?.CallerId, campaignName = call?.Campaign, h.WentToAllSupervisors, h.NotifiedIds,
            h.ClaimedById, claimedByName = h.ClaimedById is { } s ? names.GetValueOrDefault(s) : null, h.ChannelId, h.CreatedAt, h.ClosedAt,
        };
    }

    // ── Bootstrap ────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> Bootstrap(HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IAgentStateStore states, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.Ok(new { enabled = false, message = NotEnabled });

        await using var db = dbf.Create();
        await EnsureRoleMembershipsAsync(db, me.Id, ct);

        var agents = await db.Agents.AsNoTracking().Where(a => a.IsActive)
            .Select(a => new { a.Id, a.FirstName, a.LastName, a.Email, a.RoleId, LegacyRole = a.Role,
                RoleName = db.Roles.Where(r => r.Id == a.RoleId).Select(r => r.Name).FirstOrDefault() })
            .ToListAsync(ct);
        var stateList = await Task.WhenAll(agents.Select(async a => (a.Id, State: await states.GetAsync(me.TenantId, a.Id, ct))));
        var stateBy = stateList.ToDictionary(x => x.Id, x => x.State);

        var myRoleId = agents.FirstOrDefault(a => a.Id == me.Id)?.RoleId;
        var memberships = await db.ChatMembers.AsNoTracking().Where(m => m.AgentId == me.Id && m.LeftAt == null).ToListAsync(ct);
        var channelIds = memberships.Select(m => m.ChannelId).ToList();
        var channels = await db.ChatChannels.AsNoTracking().Where(c => channelIds.Contains(c.Id)).ToListAsync(ct);
        var allMembers = await db.ChatMembers.AsNoTracking().Where(m => channelIds.Contains(m.ChannelId) && m.LeftAt == null)
            .Select(m => new { m.ChannelId, m.AgentId }).ToListAsync(ct);
        var membersBy = allMembers.ToLookup(m => m.ChannelId, m => m.AgentId);
        var counts = await UnreadAsync(db, memberships.Select(m => (m.ChannelId, m.LastReadAt)).ToList(), me.Id, ct);

        var supervisorIds = await db.AgentSupervisors.AsNoTracking().Where(s => s.AgentId == me.Id).Select(s => s.SupervisorId).ToListAsync(ct);
        var myHelp = await db.HelpRequests.AsNoTracking().Where(h => h.AgentId == me.Id && h.Status == HelpRequestStatus.Open)
            .OrderByDescending(h => h.CreatedAt).FirstOrDefaultAsync(ct);
        var queue = await db.HelpRequests.AsNoTracking()
            .Where(h => h.Status == HelpRequestStatus.Open && h.AgentId != me.Id && h.NotifiedIds.Contains(me.Id))   // same people the live alert went to
            .OrderBy(h => h.CreatedAt).Take(50).ToListAsync(ct);

        return Results.Ok(new
        {
            enabled = true,
            me = new { id = me.Id, isManager = me.IsManager, isSupervisor = me.IsSupervisor, roleId = myRoleId },
            users = agents.Select(a => new
            {
                a.Id, name = $"{a.FirstName} {a.LastName}".Trim(), a.Email,
                roleName = a.RoleName ?? a.LegacyRole,
                state = stateBy.GetValueOrDefault(a.Id) is { } s ? new { code = s.Code, s.Label, since = s.SetAt } : null,
            }),
            channels = channels.Select(c =>
            {
                var m = memberships.First(x => x.ChannelId == c.Id);
                return ChannelDto(c, m, me, myRoleId, membersBy[c.Id].ToList(), counts.GetValueOrDefault(c.Id).Unread, counts.GetValueOrDefault(c.Id).Mentions);
            }),
            supervisorIds,
            myHelp = myHelp is null ? null : await HelpDto(db, myHelp, ct),
            helpQueue = await Task.WhenAll(queue.Select(h => HelpDto(db, h, ct))),
        });
    }

    // ── Channels ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> Browse(HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var mine = db.ChatMembers.Where(m => m.AgentId == me.Id && m.LeftAt == null).Select(m => m.ChannelId);
        var list = await db.ChatChannels.AsNoTracking()
            .Where(c => c.Kind == ChatChannelKind.Channel && !c.IsPrivate && !mine.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.Description, retired = c.RetiredAt != null,
                memberCount = db.ChatMembers.Count(m => m.ChannelId == c.Id && m.LeftAt == null) })
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> Join(Guid id, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var c = await db.ChatChannels.FirstOrDefaultAsync(x => x.Id == id && x.Kind == ChatChannelKind.Channel, ct);
        if (c is null || c.IsPrivate) return Results.NotFound(new { error = "Channel not found." });
        var row = await db.ChatMembers.FirstOrDefaultAsync(m => m.ChannelId == id && m.AgentId == me.Id, ct);
        if (row is null) db.ChatMembers.Add(ChatMember.Create(id, me.Id, assigned: false));
        else row.Rejoin(assigned: false);
        await db.SaveChangesAsync(ct);
        var dto = await ChannelForAsync(db, id, me, ct);
        await Push(hub, [me.Id], "channel", dto!);
        await PushMembersChanged(db, hub, id, me, ct);
        return Results.Ok(dto);
    }

    private static async Task<IResult> Leave(Guid id, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var c = await db.ChatChannels.FirstOrDefaultAsync(x => x.Id == id, ct);
        var m = c is null ? null : await Membership(db, id, me.Id, ct);
        if (c is null || m is null) return Results.NotFound(new { error = "You're not in that channel." });
        if (c.IsDirect) return Results.BadRequest(new { error = "Direct messages can't be left." });
        var roleId = await db.Agents.Where(a => a.Id == me.Id).Select(a => a.RoleId).FirstOrDefaultAsync(ct);
        if (c.MembershipLocked && (m.IsAssigned || (roleId is { } r && c.AssignedRoleIds.Contains(r))))
            return Results.Conflict(new { error = "You've been assigned to this channel, so you can't leave it." });
        m.Leave();
        await db.SaveChangesAsync(ct);
        await Push(hub, [me.Id], "channel-removed", new { channelId = id });
        await PushMembersChanged(db, hub, id, me, ct);
        return Results.NoContent();
    }

    /// <summary>Everyone still in the channel gets its refreshed member list.</summary>
    private static async Task PushMembersChanged(TenantDbContext db, IHubContext<ChatHub, IChatHubClient> hub, Guid channelId, Me me, CancellationToken ct)
    {
        var members = await ActiveMemberIds(db, channelId, ct);
        await Push(hub, members, "members", new { channelId, memberIds = members });
    }

    // ── Messages ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Top-level messages, newest page first (returned oldest→newest). <paramref name="before"/> pages back.</summary>
    private static async Task<IResult> Messages(Guid id, DateTimeOffset? before, int? limit, HttpContext http, TenantContext tc,
        ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var c = await db.ChatChannels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Results.NotFound();
        // Members read; anyone may preview a public channel before joining.
        if (await Membership(db, id, me.Id, ct) is null && (c.IsPrivate || c.IsDirect)) return Results.NotFound();
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var page = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ChannelId == id && m.ParentId == null && (before == null || m.CreatedAt < before))
            .OrderByDescending(m => m.CreatedAt).Take(take + 1).ToListAsync(ct);
        var more = page.Count > take;
        var list = page.Take(take).Reverse().ToList();
        return Results.Ok(new { messages = await WithReactionsAsync(db, list, ct), hasMore = more });
    }

    private static async Task<IResult> Thread(Guid id, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var parent = await db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id && m.ParentId == null, ct);
        if (parent is null) return Results.NotFound();
        var c = await db.ChatChannels.AsNoTracking().FirstAsync(x => x.Id == parent.ChannelId, ct);
        if (await Membership(db, c.Id, me.Id, ct) is null && (c.IsPrivate || c.IsDirect)) return Results.NotFound();
        var replies = await db.ChatMessages.AsNoTracking().Where(m => m.ParentId == id).OrderBy(m => m.CreatedAt).Take(500).ToListAsync(ct);
        var all = await WithReactionsAsync(db, [parent, .. replies], ct);
        return Results.Ok(new { parent = all[0], replies = all.Skip(1) });
    }

    private static async Task<IResult> Post(Guid id, PostChatMessageRequest req, HttpContext http, TenantContext tc,
        ScopedTenantDbContextFactory dbf, IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var c = await db.ChatChannels.FirstOrDefaultAsync(x => x.Id == id, ct);
        var member = c is null ? null : await Membership(db, id, me.Id, ct);
        if (c is null || member is null) return Results.NotFound(new { error = "Join the channel to post in it." });
        // The person's role now, not as of their sign-in token — a role change applies straight away.
        var myRole = await db.Agents.Where(a => a.Id == me.Id).Select(a => a.RoleId).FirstOrDefaultAsync(ct);
        if (c.PostBlockedReason(me.Id, myRole, me.IsManager) is { } blocked) return Results.Conflict(new { error = blocked });

        ChatMessage? parent = null;
        if (req.ParentId is { } pid)
        {
            parent = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == pid && m.ChannelId == id && m.ParentId == null, ct);
            if (parent is null) return Results.BadRequest(new { error = "That thread no longer exists." });
        }

        ChatMessage msg;
        try
        {
            if (req.Format == ChatMessageFormat.Html)
            {
                var rich = await CleanRichAsync(db, me, req.Body, ct);
                if (rich.Error is not null) return Results.BadRequest(new { error = rich.Error });
                msg = ChatMessage.CreateRich(id, me.Id, rich.Html!, rich.Text!, rich.HasImages, req.ParentId);
            }
            else msg = ChatMessage.Create(id, me.Id, req.Body, req.ParentId);
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        db.ChatMessages.Add(msg);
        parent?.AddReply(msg.CreatedAt);
        c.Touch(msg.CreatedAt);
        member.MarkRead(msg.CreatedAt);
        await db.SaveChangesAsync(ct);

        var dto = MessageDto(msg, []);
        var members = await ActiveMemberIds(db, id, ct);
        await Push(hub, members, "message", dto);
        if (parent is not null) await Push(hub, members, "message-updated", MessageDto(parent, await db.ChatReactions.AsNoTracking().Where(r => r.MessageId == parent.Id).ToListAsync(ct)));
        return Results.Ok(dto);
    }

    private static async Task<IResult> Edit(Guid id, PostChatMessageRequest req, HttpContext http, TenantContext tc,
        ScopedTenantDbContextFactory dbf, IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var msg = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (msg is null || msg.AgentId != me.Id || msg.DeletedAt is not null) return Results.NotFound();
        var c = await db.ChatChannels.FirstAsync(x => x.Id == msg.ChannelId, ct);
        if (c.IsRetired) return Results.Conflict(new { error = "This channel is retired — its history is read-only." });
        try
        {
            if (req.Format == ChatMessageFormat.Html)
            {
                var rich = await CleanRichAsync(db, me, req.Body, ct);
                if (rich.Error is not null) return Results.BadRequest(new { error = rich.Error });
                msg.EditRich(rich.Html!, rich.Text!, rich.HasImages);
            }
            else msg.Edit(req.Body);
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        await db.SaveChangesAsync(ct);
        var dto = MessageDto(msg, await db.ChatReactions.AsNoTracking().Where(r => r.MessageId == id).ToListAsync(ct));
        await Push(hub, await ActiveMemberIds(db, msg.ChannelId, ct), "message-updated", dto);
        return Results.Ok(dto);
    }

    private static async Task<IResult> Delete(Guid id, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var msg = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (msg is null || msg.DeletedAt is not null) return Results.NotFound();
        if (msg.AgentId != me.Id)
        {
            var channel = await db.ChatChannels.AsNoTracking().FirstAsync(x => x.Id == msg.ChannelId, ct);
            var roleId = await db.Agents.Where(a => a.Id == me.Id).Select(a => a.RoleId).FirstOrDefaultAsync(ct);
            if (!channel.CanDeleteOthers(me.Id, roleId, me.IsManager))
                return Results.Json(new { error = "You can only delete your own messages here." }, statusCode: 403);
        }
        msg.Delete();
        ChatMessage? parent = msg.ParentId is { } pid ? await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == pid, ct) : null;
        parent?.RemoveReply();
        await db.SaveChangesAsync(ct);
        var members = await ActiveMemberIds(db, msg.ChannelId, ct);
        await Push(hub, members, "message-updated", MessageDto(msg, []));
        if (parent is not null) await Push(hub, members, "message-updated", MessageDto(parent, await db.ChatReactions.AsNoTracking().Where(r => r.MessageId == parent.Id).ToListAsync(ct)));
        return Results.NoContent();
    }

    private static async Task<IResult> React(Guid id, ReactRequest req, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var msg = await db.ChatMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null, ct);
        if (msg is null || await Membership(db, msg.ChannelId, me.Id, ct) is null) return Results.NotFound();
        var c = await db.ChatChannels.AsNoTracking().FirstAsync(x => x.Id == msg.ChannelId, ct);
        if (c.IsRetired) return Results.Conflict(new { error = "This channel is retired — its history is read-only." });
        var emoji = (req.Emoji ?? "").Trim();
        var existing = await db.ChatReactions.FirstOrDefaultAsync(r => r.MessageId == id && r.AgentId == me.Id && r.Emoji == emoji, ct);
        if (existing is not null) db.ChatReactions.Remove(existing);
        else
        {
            try { db.ChatReactions.Add(ChatReaction.Create(id, me.Id, emoji)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }
        await db.SaveChangesAsync(ct);
        var dto = MessageDto(msg, await db.ChatReactions.AsNoTracking().Where(r => r.MessageId == id).ToListAsync(ct));
        await Push(hub, await ActiveMemberIds(db, msg.ChannelId, ct), "message-updated", dto);
        return Results.Ok(dto);
    }

    private static async Task<IResult> MarkRead(Guid id, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var m = await Membership(db, id, me.Id, ct);
        if (m is null) return Results.NotFound();
        m.MarkRead(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        await Push(hub, [me.Id], "read", new { channelId = id, at = m.LastReadAt });   // the person's other tabs
        return Results.NoContent();
    }

    private static async Task<IResult> Typing(Guid id, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null || !Enabled(tc)) return Results.NoContent();
        await using var db = dbf.Create();
        var members = await ActiveMemberIds(db, id, ct);
        if (!members.Contains(me.Id)) return Results.NoContent();
        await Push(hub, members.Where(x => x != me.Id), "typing", new { channelId = id, agentId = me.Id });
        return Results.NoContent();
    }

    // ── Pins ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>What's pinned in a conversation: for everyone (newest first) and this person's own pins.</summary>
    private static async Task<IResult> Pins(Guid id, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var c = await db.ChatChannels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || (await Membership(db, id, me.Id, ct) is null && (c.IsPrivate || c.IsDirect))) return Results.NotFound();
        var everyone = await db.ChatMessages.AsNoTracking().Where(m => m.ChannelId == id && m.PinnedAt != null && m.DeletedAt == null)
            .OrderByDescending(m => m.PinnedAt).Take(50).ToListAsync(ct);
        var mineIds = db.ChatPersonalPins.Where(p => p.AgentId == me.Id && p.ChannelId == id).Select(p => p.MessageId);
        var mine = await db.ChatMessages.AsNoTracking().Where(m => mineIds.Contains(m.Id) && m.DeletedAt == null)
            .OrderByDescending(m => m.CreatedAt).Take(50).ToListAsync(ct);
        return Results.Ok(new { everyone = await WithReactionsAsync(db, everyone, ct), mine = await WithReactionsAsync(db, mine, ct) });
    }

    /// <param name="scope">"me" (pin for yourself) or "everyone" (needs the channel's pin permission).</param>
    private static async Task<IResult> SetPin(Guid id, string? scope, bool pin, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var msg = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (msg is null || await Membership(db, msg.ChannelId, me.Id, ct) is null) return Results.NotFound();
        if (pin && msg.DeletedAt is not null) return Results.BadRequest(new { error = "A deleted message can't be pinned." });

        if (scope == "everyone")
        {
            var c = await db.ChatChannels.AsNoTracking().FirstAsync(x => x.Id == msg.ChannelId, ct);
            var roleId = await db.Agents.Where(a => a.Id == me.Id).Select(a => a.RoleId).FirstOrDefaultAsync(ct);
            if (!c.CanPinForEveryone(me.Id, roleId, me.IsManager))
                return Results.Conflict(new { error = c.IsRetired ? "This channel is retired — its history is read-only." : "You can't pin messages for everyone in this channel." });
            if (pin) msg.Pin(me.Id); else msg.Unpin();
            await db.SaveChangesAsync(ct);
            var members = await ActiveMemberIds(db, msg.ChannelId, ct);
            var dto = MessageDto(msg, await db.ChatReactions.AsNoTracking().Where(r => r.MessageId == id).ToListAsync(ct));
            await Push(hub, members, "message-updated", dto);
            await Push(hub, members, "pins", new { channelId = msg.ChannelId });
            return Results.Ok(dto);
        }

        var existing = await db.ChatPersonalPins.FirstOrDefaultAsync(p => p.AgentId == me.Id && p.MessageId == id, ct);
        if (pin && existing is null) db.ChatPersonalPins.Add(ChatPersonalPin.Create(me.Id, id, msg.ChannelId));
        if (!pin && existing is not null) db.ChatPersonalPins.Remove(existing);
        await db.SaveChangesAsync(ct);
        await Push(hub, [me.Id], "pins", new { channelId = msg.ChannelId });   // the person's other tabs
        return Results.NoContent();
    }

    // ── Direct messages ──────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> OpenDirect(OpenDirectRequest req, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var (channel, error) = await FindOrCreateDirectAsync(db, me.TenantId, me.Id, req.AgentIds ?? [], ct);
        if (channel is null) return Results.BadRequest(new { error });
        var members = await ActiveMemberIds(db, channel.Id, ct);
        foreach (var m in members) await Push(hub, [m], "channel", (await ChannelForAsync(db, channel.Id, me with { Id = m }, ct))!);
        return Results.Ok(await ChannelForAsync(db, channel.Id, me, ct));
    }

    private static async Task<(ChatChannel? Channel, string? Error)> FindOrCreateDirectAsync(
        TenantDbContext db, Guid tenantId, Guid me, IEnumerable<Guid> others, CancellationToken ct)
    {
        var ids = others.Append(me).Distinct().ToList();
        if (ids.Count < 2) return (null, "Choose who to message.");
        if (ids.Count > 9) return (null, "A group message is at most 9 people — create a channel for more.");
        var found = await db.Agents.CountAsync(a => ids.Contains(a.Id) && a.IsActive, ct);
        if (found != ids.Count) return (null, "Someone in that list isn't an active user.");
        var key = ChatChannel.DirectKeyFor(ids);
        var existing = await db.ChatChannels.FirstOrDefaultAsync(c => c.DirectKey == key, ct);
        if (existing is not null) return (existing, null);
        var channel = ChatChannel.CreateDirect(tenantId, ids, me);
        db.ChatChannels.Add(channel);
        foreach (var id in ids) db.ChatMembers.Add(ChatMember.Create(channel.Id, id, assigned: true));
        await db.SaveChangesAsync(ct);
        return (channel, null);
    }

    // ── Formatted messages + images ──────────────────────────────────────────────────────────────────────────────

    private const int MaxImagesPerMessage = 10;

    private sealed record RichBody(string? Html, string? Text, bool HasImages, string? Error);

    /// <summary>Sanitizes composer HTML and checks every image it references is an upload in this account.</summary>
    private static async Task<RichBody> CleanRichAsync(TenantDbContext db, Me me, string html, CancellationToken ct)
    {
        var clean = ChatRichText.Clean(html);
        if (clean.FileIds.Count > MaxImagesPerMessage) return new(null, null, false, $"At most {MaxImagesPerMessage} images per message.");
        if (clean.FileIds.Count > 0)
        {
            var found = await db.ChatFiles.CountAsync(f => clean.FileIds.Contains(f.Id) && f.TenantId == me.TenantId, ct);
            if (found != clean.FileIds.Count) return new(null, null, false, "An image in that message is no longer available — paste it again.");
        }
        return new(clean.Html, clean.Text, clean.FileIds.Count > 0, null);
    }

    /// <summary>A pasted / attached image (raw body). Only PNG, JPEG, GIF and WebP, checked by content, up to 5 MB.</summary>
    private static async Task<IResult> UploadFile(HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf, IBlobStorage blobs, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        using var buffer = new MemoryStream();
        var limited = new byte[81920];
        int read;
        while ((read = await http.Request.Body.ReadAsync(limited, ct)) > 0)
        {
            buffer.Write(limited, 0, read);
            if (buffer.Length > ChatFile.MaxBytes) return Results.BadRequest(new { error = "Images can be up to 5 MB." });
        }
        if (buffer.Length == 0) return Results.BadRequest(new { error = "Nothing was uploaded." });
        var type = ChatFile.SniffImageType(buffer.GetBuffer().AsSpan(0, (int)Math.Min(16, buffer.Length)));
        if (type is null) return Results.BadRequest(new { error = "Only PNG, JPEG, GIF and WebP images can be shared." });

        var file = ChatFile.Create(me.TenantId, me.Id, type, buffer.Length);
        buffer.Position = 0;
        await blobs.PutAsync(file.StorageKey, buffer, type, ct);
        await using var db = dbf.Create();
        db.ChatFiles.Add(file);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { file.Id, file.ContentType, file.SizeBytes });
    }

    /// <summary>An image, to its uploader or anyone who can see a message that uses it.</summary>
    private static async Task<IResult> GetFile(Guid id, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf, IBlobStorage blobs, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound();
        await using var db = dbf.Create();
        var file = await db.ChatFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id && f.TenantId == me.TenantId, ct);
        if (file is null) return Results.NotFound();
        if (file.AgentId != me.Id)
        {
            var token = $"data-chat-file=\"{id}\"";
            var mine = db.ChatMembers.Where(m => m.AgentId == me.Id && m.LeftAt == null).Select(m => m.ChannelId);
            var publicChannels = db.ChatChannels.Where(c => c.Kind == ChatChannelKind.Channel && !c.IsPrivate).Select(c => c.Id);
            var visible = await db.ChatMessages.AnyAsync(m => m.DeletedAt == null && m.Body.Contains(token)
                && (mine.Contains(m.ChannelId) || publicChannels.Contains(m.ChannelId)), ct);
            if (!visible) return Results.NotFound();
        }
        var stream = await blobs.OpenReadAsync(file.StorageKey, ct);
        if (stream is null) return Results.NotFound();
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["Cache-Control"] = "private, max-age=86400";
        return Results.Stream(stream, file.ContentType);
    }

    // ── Search ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> Search(string q, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        var term = (q ?? "").Trim();
        if (term.Length < 2) return Results.Ok(Array.Empty<object>());
        await using var db = dbf.Create();
        var mine = db.ChatMembers.Where(m => m.AgentId == me.Id && m.LeftAt == null).Select(m => m.ChannelId);
        var like = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var hits = await db.ChatMessages.AsNoTracking()
            .Where(m => mine.Contains(m.ChannelId) && m.DeletedAt == null && EF.Functions.ILike(m.BodyText, like))
            .OrderByDescending(m => m.CreatedAt).Take(50).ToListAsync(ct);
        return Results.Ok(await WithReactionsAsync(db, hits, ct));
    }

    // ── Raise a hand ─────────────────────────────────────────────────────────────────────────────────────────────

    private static bool OnDuty(AgentStateEntry? s) => s is not null && s.Code != AgentStateCodes.LoggedOut;

    private static async Task<IResult> RaiseHand(RaiseHandRequest req, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IAgentStateStore states, IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();

        var open = await db.HelpRequests.FirstOrDefaultAsync(h => h.AgentId == me.Id && h.Status == HelpRequestStatus.Open, ct);
        if (open is not null) return Results.Ok(await HelpDto(db, open, ct));

        // Their own supervisors who are on duty; when none is, every on-duty supervisor; when nobody is on duty at all,
        // their own supervisors anyway — the request waits for them in the queue.
        var mine = await db.AgentSupervisors.Where(s => s.AgentId == me.Id).Select(s => s.SupervisorId).ToListAsync(ct);
        var mineOnDuty = new List<Guid>();
        foreach (var s in mine) if (OnDuty(await states.GetAsync(me.TenantId, s, ct))) mineOnDuty.Add(s);
        var notified = mineOnDuty;
        var wentToAll = false;
        if (notified.Count == 0)
        {
            var everySupervisor = await SupervisorIdsAsync(db, me.Id, ct);
            var onDuty = new List<Guid>();
            foreach (var s in everySupervisor) if (OnDuty(await states.GetAsync(me.TenantId, s, ct))) onDuty.Add(s);
            if (onDuty.Count > 0) { notified = onDuty; wentToAll = true; }
            else notified = mine.Count > 0 ? mine : everySupervisor;
        }

        var callId = req.CallRecordId is { } cid && await db.CallRecords.AnyAsync(r => r.Id == cid, ct) ? cid : (Guid?)null;
        var help = HelpRequest.Create(me.TenantId, me.Id, req.Note, callId, notified, wentToAll);
        db.HelpRequests.Add(help);
        await db.SaveChangesAsync(ct);
        var dto = await HelpDto(db, help, ct);
        await Push(hub, notified.Append(me.Id), "help", dto);
        return Results.Ok(dto);
    }

    /// <summary>Everyone (else) who supervises: a custom role with supervisor.monitor, or the legacy supervisor / admin role.</summary>
    private static async Task<List<Guid>> SupervisorIdsAsync(TenantDbContext db, Guid except, CancellationToken ct)
    {
        // Permissions are a converted JSON column — filter the (few) roles in memory.
        var roleIds = (await db.Roles.AsNoTracking().ToListAsync(ct))
            .Where(r => r.HasPermission(Permission.SupervisorMonitor)).Select(r => r.Id).ToList();
        return await db.Agents.AsNoTracking()
            .Where(a => a.IsActive && a.Id != except
                && (a.RoleId != null ? roleIds.Contains(a.RoleId.Value) : (a.Role == AgentRole.Supervisor || a.Role == AgentRole.Admin)))
            .Select(a => a.Id).ToListAsync(ct);
    }

    private static async Task<IResult> ClaimHelp(Guid id, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var help = await db.HelpRequests.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (help is null) return Results.NotFound();
        if (!help.NotifiedIds.Contains(me.Id) && !me.IsSupervisor) return Results.Forbid();
        if (!help.IsOpen) return Results.Conflict(new { error = "Someone already picked this up.", help = await HelpDto(db, help, ct) });

        var (channel, error) = await FindOrCreateDirectAsync(db, me.TenantId, me.Id, [help.AgentId], ct);
        if (channel is null) return Results.BadRequest(new { error });

        // First one wins: the claim only lands if the row is still open.
        var won = await db.HelpRequests.Where(h => h.Id == id && h.Status == HelpRequestStatus.Open)
            .ExecuteUpdateAsync(s => s
                .SetProperty(h => h.Status, HelpRequestStatus.Claimed)
                .SetProperty(h => h.ClaimedById, me.Id)
                .SetProperty(h => h.ChannelId, channel.Id)
                .SetProperty(h => h.ClosedAt, DateTimeOffset.UtcNow), ct);
        await db.Entry(help).ReloadAsync(ct);
        if (won == 0) return Results.Conflict(new { error = "Someone already picked this up.", help = await HelpDto(db, help, ct) });

        var names = await db.Agents.AsNoTracking().Where(a => a.Id == me.Id || a.Id == help.AgentId)
            .ToDictionaryAsync(a => a.Id, a => a.FirstName, ct);
        var text = $"{names.GetValueOrDefault(me.Id)} picked up {names.GetValueOrDefault(help.AgentId)}'s request for help."
                   + (help.Note is null ? "" : $" Note: “{help.Note}”");
        var system = ChatMessage.Create(channel.Id, null, text, null, ChatMessageKind.System);
        db.ChatMessages.Add(system);
        channel.Touch(system.CreatedAt);
        await db.SaveChangesAsync(ct);

        var dto = await HelpDto(db, help, ct);
        await Push(hub, help.NotifiedIds.Append(help.AgentId).Append(me.Id), "help", dto);
        foreach (var m in new[] { me.Id, help.AgentId }) await Push(hub, [m], "channel", (await ChannelForAsync(db, channel.Id, me with { Id = m }, ct))!);
        await Push(hub, [me.Id, help.AgentId], "message", MessageDto(system, []));
        return Results.Ok(dto);
    }

    private static async Task<IResult> CancelHelp(Guid id, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        var me = WhoAmI(http, tc);
        if (me is null) return Results.Unauthorized();
        if (!Enabled(tc)) return Results.NotFound(new { error = NotEnabled });
        await using var db = dbf.Create();
        var help = await db.HelpRequests.FirstOrDefaultAsync(h => h.Id == id && h.AgentId == me.Id, ct);
        if (help is null) return Results.NotFound();
        help.Cancel();
        await db.SaveChangesAsync(ct);
        var dto = await HelpDto(db, help, ct);
        await Push(hub, help.NotifiedIds.Append(me.Id), "help", dto);
        return Results.Ok(dto);
    }

    // ── Chat administration (chat.manage) ────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> AdminList(TenantContext tc, ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var channels = await db.ChatChannels.AsNoTracking().Where(c => c.Kind == ChatChannelKind.Channel).OrderBy(c => c.Name).ToListAsync(ct);
        var ids = channels.Select(c => c.Id).ToList();
        var members = await db.ChatMembers.AsNoTracking().Where(m => ids.Contains(m.ChannelId) && m.LeftAt == null)
            .Select(m => new { m.ChannelId, m.AgentId, m.IsAssigned }).ToListAsync(ct);
        var by = members.ToLookup(m => m.ChannelId);
        return Results.Ok(new
        {
            enabled = Enabled(tc),
            channels = channels.Select(c => new
            {
                c.Id, c.Name, c.Description, c.IsPrivate, c.PostingRestricted, c.PosterIds, c.PosterRoleIds, c.MembershipLocked, c.AssignedRoleIds,
                c.PinnerIds, c.PinnerRoleIds, c.ModeratorIds, c.ModeratorRoleIds,
                retired = c.IsRetired, c.RetiredAt, c.CreatedAt, c.LastMessageAt,
                assignedIds = by[c.Id].Where(m => m.IsAssigned).Select(m => m.AgentId),
                memberCount = by[c.Id].Count(),
            }),
        });
    }

    private static async Task<IResult> AdminCreate(SaveChannelRequest req, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        Guid.TryParse(http.User.FindFirst("sub")?.Value, out var by);
        await using var db = dbf.Create();
        ChatChannel c;
        try { c = ChatChannel.CreateChannel(tc.Current!.Id, req.Name, req.Description, req.IsPrivate, by == Guid.Empty ? null : by); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        if (await db.ChatChannels.AnyAsync(x => x.Kind == ChatChannelKind.Channel && x.Name.ToLower() == c.Name.ToLower(), ct))
            return Results.Conflict(new { error = $"There's already a channel called {c.Name}." });
        db.ChatChannels.Add(c);
        return await ApplyConfigAsync(db, c, req, tc, hub, ct, isNew: true);
    }

    private static async Task<IResult> AdminUpdate(Guid id, SaveChannelRequest req, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var c = await db.ChatChannels.FirstOrDefaultAsync(x => x.Id == id && x.Kind == ChatChannelKind.Channel, ct);
        if (c is null) return Results.NotFound();
        try { c.Rename(req.Name, req.Description); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        if (await db.ChatChannels.AnyAsync(x => x.Id != id && x.Kind == ChatChannelKind.Channel && x.Name.ToLower() == c.Name.ToLower(), ct))
            return Results.Conflict(new { error = $"There's already a channel called {c.Name}." });
        return await ApplyConfigAsync(db, c, req, tc, hub, ct, isNew: false);
    }

    /// <summary>Settings + assigned members: named people and everyone holding an assigned role are assigned members;
    /// people taken off the assigned list leave a private channel (a public one keeps them as ordinary members).</summary>
    private static async Task<IResult> ApplyConfigAsync(TenantDbContext db, ChatChannel c, SaveChannelRequest req, TenantContext tc,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct, bool isNew)
    {
        var roleIds = (req.AssignedRoleIds ?? []).Concat(req.PosterRoleIds ?? []).Concat(req.PinnerRoleIds ?? [])
            .Concat(req.ModeratorRoleIds ?? []).Distinct().ToList();
        var existingRoles = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        var validRoles = (req.AssignedRoleIds ?? []).Where(existingRoles.Contains).Distinct().ToList();
        var posterRoles = (req.PosterRoleIds ?? []).Where(existingRoles.Contains).Distinct().ToList();
        c.Configure(req.IsPrivate, req.PostingRestricted, req.PosterIds ?? [], req.MembershipLocked, validRoles, posterRoles);
        c.SetPinners(req.PinnerIds ?? [], (req.PinnerRoleIds ?? []).Where(existingRoles.Contains));
        c.SetModerators(req.ModeratorIds ?? [], (req.ModeratorRoleIds ?? []).Where(existingRoles.Contains));

        var named = (req.AssignedIds ?? []).Distinct().ToList();
        var byRole = validRoles.Count == 0 ? [] : await db.Agents.Where(a => a.IsActive && a.RoleId != null && validRoles.Contains(a.RoleId.Value)).Select(a => a.Id).ToListAsync(ct);
        var assigned = named.Concat(byRole).Distinct().ToHashSet();
        var existingAgents = await db.Agents.Where(a => assigned.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct);
        assigned.IntersectWith(existingAgents);

        var rows = isNew ? [] : await db.ChatMembers.Where(m => m.ChannelId == c.Id).ToListAsync(ct);
        var before = rows.Where(r => r.IsActive).Select(r => r.AgentId).ToHashSet();
        foreach (var id in assigned)
        {
            var row = rows.FirstOrDefault(r => r.AgentId == id);
            if (row is null) db.ChatMembers.Add(ChatMember.Create(c.Id, id, assigned: true));
            else row.Rejoin(assigned: true);
        }
        foreach (var row in rows.Where(r => r.IsAssigned && !assigned.Contains(r.AgentId)))
        {
            row.SetAssigned(false);
            if (c.IsPrivate && row.IsActive) row.Leave();
        }
        await db.SaveChangesAsync(ct);

        var after = await ActiveMemberIds(db, c.Id, ct);
        var me = new Me(Guid.Empty, tc.Current!.Id, false, false);
        // canPost in these pushes is computed as a non-manager; clients recompute it from the flags + posterIds with their own role.
        foreach (var m in after)
            await Push(hub, [m], "channel", (await ChannelForAsync(db, c.Id, me with { Id = m }, ct))!);
        await Push(hub, before.Except(after), "channel-removed", new { channelId = c.Id });
        return Results.Ok(new { c.Id });
    }

    private static async Task<IResult> SetRetired(Guid id, bool retired, ScopedTenantDbContextFactory dbf, TenantContext tc,
        IHubContext<ChatHub, IChatHubClient> hub, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var c = await db.ChatChannels.FirstOrDefaultAsync(x => x.Id == id && x.Kind == ChatChannelKind.Channel, ct);
        if (c is null) return Results.NotFound();
        if (retired) c.Retire(); else c.Unretire();
        await db.SaveChangesAsync(ct);
        var me = new Me(Guid.Empty, tc.Current!.Id, false, false);
        foreach (var m in await ActiveMemberIds(db, id, ct))
            await Push(hub, [m], "channel", (await ChannelForAsync(db, id, me with { Id = m }, ct))!);
        return Results.NoContent();
    }

    // ── Assigned supervisors ─────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetSupervisors(Guid id, TenantContext tc, ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        return Results.Ok(await db.AgentSupervisors.AsNoTracking().Where(s => s.AgentId == id).Select(s => s.SupervisorId).ToListAsync(ct));
    }

    private static async Task<IResult> SetSupervisors(Guid id, SetSupervisorsRequest req, TenantContext tc, ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (!await db.Agents.AnyAsync(a => a.Id == id, ct)) return Results.NotFound();
        var wanted = (req.SupervisorIds ?? []).Where(s => s != id).Distinct().ToList();
        var valid = await db.Agents.Where(a => wanted.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct);
        var current = await db.AgentSupervisors.Where(s => s.AgentId == id).ToListAsync(ct);
        db.AgentSupervisors.RemoveRange(current.Where(s => !valid.Contains(s.SupervisorId)));
        foreach (var s in valid.Where(v => current.All(c => c.SupervisorId != v)))
            db.AgentSupervisors.Add(AgentSupervisor.Create(id, s));
        await db.SaveChangesAsync(ct);
        return Results.Ok(valid);
    }
}

public record PostChatMessageRequest(string Body, Guid? ParentId = null, string? Format = null);
public record ReactRequest(string Emoji);
public record OpenDirectRequest(List<Guid>? AgentIds);
public record RaiseHandRequest(string? Note, Guid? CallRecordId);
public record SaveChannelRequest(string Name, string? Description, bool IsPrivate, bool PostingRestricted, List<Guid>? PosterIds,
    bool MembershipLocked, List<Guid>? AssignedRoleIds, List<Guid>? AssignedIds, List<Guid>? PosterRoleIds = null,
    List<Guid>? PinnerIds = null, List<Guid>? PinnerRoleIds = null, List<Guid>? ModeratorIds = null, List<Guid>? ModeratorRoleIds = null);
public record PinRequest(string? Scope);
public record SetSupervisorsRequest(List<Guid>? SupervisorIds);
