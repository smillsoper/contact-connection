using System.Collections.Concurrent;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Hubs;

/// <summary>
/// Live screen view (S183), at /hubs/screen-view. A supervisor watches an agent's shared screen and can point at things
/// on it. Only the WebRTC set-up (offer / answer / ICE) and pointer clicks pass through here — the video goes browser to
/// browser (or through our TURN relay). Every message is checked against the session: only its viewer and its agent
/// may speak in it. The agent's portal shows a banner the whole time; each session is audited (screen_view_sessions).
/// </summary>
[Authorize]
public class ScreenViewHub(ITenantDbContextFactory dbs, ScreenViewRegistry registry, ILogger<ScreenViewHub> logger) : Hub<IScreenViewClient>
{
    public static string AgentGroup(Guid agentId) => $"screen-agent:{agentId}";

    private Guid? Me => Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var id) ? id : null;
    private string? Schema => Context.User?.FindFirst("tenant_schema")?.Value;

    public override async Task OnConnectedAsync()
    {
        if (Me is { } me) await Groups.AddToGroupAsync(Context.ConnectionId, AgentGroup(me));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var s in registry.ForConnection(Context.ConnectionId))
            await EndAsync(s, s.ViewerConnection == Context.ConnectionId ? "The supervisor closed the view" : "The agent's portal closed");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Supervisor: ask to watch an agent's screen. Returns the session id.</summary>
    public async Task<string> StartView(string agentId)
    {
        var perms = (Context.User?.FindFirst("permissions")?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (!perms.Any(p => p is Permission.SupervisorMonitor or Permission.SupervisorOverride))
            throw new HubException("Viewing an agent's screen needs live monitoring permission.");
        if (Me is not { } me || Schema is not { } schema || !Guid.TryParse(agentId, out var agent))
            throw new HubException("Not signed in.");
        if (agent == me) throw new HubException("That's your own screen.");

        await using var db = dbs.Create(schema);
        // The agent must be in the viewer's own account (the tenant schema comes from the viewer's sign-in).
        var target = await db.Agents.AsNoTracking().Where(a => a.Id == agent && a.IsActive).Select(a => new { a.Id }).FirstOrDefaultAsync();
        if (target is null) throw new HubException("That agent wasn't found.");
        var viewer = await db.Agents.AsNoTracking().Where(a => a.Id == me).Select(a => (a.FirstName + " " + a.LastName).Trim()).FirstOrDefaultAsync() ?? "A supervisor";
        var tenantId = Guid.TryParse(Context.User?.FindFirst("tenant_id")?.Value, out var t) ? t : Guid.Empty;

        var audit = ScreenViewSession.Start(tenantId, agent, me, viewer);
        db.ScreenViewSessions.Add(audit);
        await db.SaveChangesAsync();

        var session = registry.Add(new ScreenView(audit.Id, schema, agent, me, viewer, Context.ConnectionId));
        logger.LogInformation("Screen view {Id}: {Viewer} asked to view agent {Agent}", audit.Id, me, agent);
        await Clients.Group(AgentGroup(agent)).ScreenViewRequested(session.Id.ToString(), viewer);
        return session.Id.ToString();
    }

    /// <summary>Agent portal: here's my screen (WebRTC offer). The first portal tab to answer owns the session.</summary>
    public async Task AgentOffer(string sessionId, string sdp)
    {
        var s = AgentSession(sessionId);
        if (s.AgentConnection is not null && s.AgentConnection != Context.ConnectionId) return;   // another tab got there first
        s.AgentConnection = Context.ConnectionId;
        await Clients.Client(s.ViewerConnection).ScreenViewOffer(sessionId, sdp);
        await AuditAsync(s, a => a.Connected());
    }

    /// <summary>Supervisor: WebRTC answer.</summary>
    public Task ViewerAnswer(string sessionId, string sdp)
    {
        var s = ViewerSession(sessionId);
        return s.AgentConnection is { } agent ? Clients.Client(agent).ScreenViewAnswer(sessionId, sdp) : Task.CompletedTask;
    }

    /// <summary>Either side: an ICE candidate for the other side.</summary>
    public Task Ice(string sessionId, string candidate)
    {
        var s = registry.Get(sessionId) ?? throw new HubException("That view has ended.");
        if (Context.ConnectionId == s.ViewerConnection)
            return s.AgentConnection is { } a ? Clients.Client(a).ScreenViewIce(sessionId, candidate) : Task.CompletedTask;
        if (Context.ConnectionId == s.AgentConnection)
            return Clients.Client(s.ViewerConnection).ScreenViewIce(sessionId, candidate);
        throw new HubException("Not part of this view.");
    }

    /// <summary>Supervisor: point at a spot (fractions of the shared screen, 0–1).</summary>
    public async Task Point(string sessionId, double x, double y)
    {
        var s = ViewerSession(sessionId);
        if (s.AgentConnection is null || x is < 0 or > 1 || y is < 0 or > 1) return;
        await Clients.Client(s.AgentConnection).ScreenViewPoint(sessionId, x, y, s.ViewerName);
        await AuditAsync(s, a => a.Pointed());
    }

    /// <summary>The drawing colours the viewer offers — anything else is refused.</summary>
    public static readonly string[] DrawColors = ["#ef4444", "#f59e0b", "#22c55e", "#3b82f6", "#a855f7", "#ffffff"];

    /// <summary>Supervisor: part of a free-hand stroke — screen fractions as x,y pairs. A stroke arrives in pieces as it's
    /// drawn (same <paramref name="strokeId"/>); the agent's portal shows it and fades it a few seconds after the last piece.</summary>
    public async Task Draw(string sessionId, string strokeId, string color, double[] points, bool first)
    {
        var s = ViewerSession(sessionId);
        if (s.AgentConnection is null || !DrawColors.Contains(color) || points.Length is 0 or > 800 || points.Length % 2 != 0
            || points.Any(p => double.IsNaN(p) || p < 0 || p > 1) || strokeId.Length is 0 or > 40)
            return;
        await Clients.Client(s.AgentConnection).ScreenViewDraw(sessionId, strokeId, color, points);
        if (first) await AuditAsync(s, a => a.Drew());
    }

    /// <summary>Supervisor: wipe everything drawn on the agent's screen.</summary>
    public Task ClearDrawing(string sessionId)
    {
        var s = ViewerSession(sessionId);
        return s.AgentConnection is { } a ? Clients.Client(a).ScreenViewClear(sessionId) : Task.CompletedTask;
    }

    /// <summary>Agent portal: declined, or couldn't share.</summary>
    public async Task Decline(string sessionId, string reason)
    {
        var s = AgentSession(sessionId);
        await EndAsync(s, string.IsNullOrWhiteSpace(reason) ? "The agent didn't share their screen" : reason);
    }

    /// <summary>Either side: end it.</summary>
    public async Task Stop(string sessionId)
    {
        if (registry.Get(sessionId) is not { } s) return;
        if (Context.ConnectionId != s.ViewerConnection && Context.ConnectionId != s.AgentConnection && Me != s.AgentId)
            throw new HubException("Not part of this view.");
        await EndAsync(s, Context.ConnectionId == s.ViewerConnection ? "The supervisor closed the view" : "The agent stopped sharing their screen");
    }

    private ScreenView AgentSession(string sessionId)
    {
        var s = registry.Get(sessionId) ?? throw new HubException("That view has ended.");
        if (Me != s.AgentId) throw new HubException("Not your screen view.");
        return s;
    }

    private ScreenView ViewerSession(string sessionId)
    {
        var s = registry.Get(sessionId) ?? throw new HubException("That view has ended.");
        if (Context.ConnectionId != s.ViewerConnection) throw new HubException("Not your screen view.");
        return s;
    }

    private async Task EndAsync(ScreenView s, string reason)
    {
        if (!registry.Remove(s.Id)) return;
        await Clients.Client(s.ViewerConnection).ScreenViewEnded(s.Id.ToString(), reason);
        // Every portal tab of the agent drops its banner / prompt (a pending request has no agent connection yet).
        await Clients.Group(AgentGroup(s.AgentId)).ScreenViewEnded(s.Id.ToString(), reason);
        await AuditAsync(s, a => a.End(reason));
        logger.LogInformation("Screen view {Id} ended: {Reason}", s.Id, reason);
    }

    private async Task AuditAsync(ScreenView s, Action<ScreenViewSession> change)
    {
        try
        {
            await using var db = dbs.Create(s.Schema);
            if (await db.ScreenViewSessions.FirstOrDefaultAsync(a => a.Id == s.Id) is { } row)
            {
                change(row);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Screen view {Id}: audit update failed", s.Id); }
    }
}

public interface IScreenViewClient
{
    Task ScreenViewRequested(string sessionId, string viewerName);
    Task ScreenViewOffer(string sessionId, string sdp);
    Task ScreenViewAnswer(string sessionId, string sdp);
    Task ScreenViewIce(string sessionId, string candidate);
    Task ScreenViewPoint(string sessionId, double x, double y, string viewerName);
    Task ScreenViewDraw(string sessionId, string strokeId, string color, double[] points);
    Task ScreenViewClear(string sessionId);
    Task ScreenViewEnded(string sessionId, string reason);
}

/// <summary>A live screen view: who's watching whom, over which connections.</summary>
public class ScreenView(Guid id, string schema, Guid agentId, Guid viewerId, string viewerName, string viewerConnection)
{
    public Guid Id { get; } = id;
    public string Schema { get; } = schema;
    public Guid AgentId { get; } = agentId;
    public Guid ViewerId { get; } = viewerId;
    public string ViewerName { get; } = viewerName;
    public string ViewerConnection { get; } = viewerConnection;
    public string? AgentConnection { get; set; }
}

/// <summary>Live screen views on this API instance (in memory — a view lives only as long as both connections).</summary>
public class ScreenViewRegistry
{
    private readonly ConcurrentDictionary<Guid, ScreenView> _views = new();

    public ScreenView Add(ScreenView v) { _views[v.Id] = v; return v; }
    public ScreenView? Get(string id) => Guid.TryParse(id, out var g) && _views.TryGetValue(g, out var v) ? v : null;
    public bool Remove(Guid id) => _views.TryRemove(id, out _);
    public List<ScreenView> ForConnection(string connectionId) =>
        _views.Values.Where(v => v.ViewerConnection == connectionId || v.AgentConnection == connectionId).ToList();
}
