using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// In-call coaching notes (S183): a supervisor sends an agent a short note; it's pinned in the agent's portal until they
/// press "Got it". Live both ways — the agent's portal gets the note at once, the supervisor sees Sent → Seen → Got it.
/// </summary>
public static class CoachingNotesEndpoints
{
    public static IEndpointRouteBuilder MapCoachingNotesEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/coaching-notes").RequireAuthorization();
        g.MapPost("", Send);
        g.MapGet("", ForAgent);
        g.MapGet("mine", Mine);
        g.MapGet("call/{callRecordId:guid}", ForCall);
        g.MapPost("{id:guid}/seen", (Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, IHubContext<FlowHub, IFlowHubClient> hub, HttpContext http, CancellationToken ct) =>
            AgentUpdate(id, n => n.Seen(), dbf, tc, hub, http, ct));
        g.MapPost("{id:guid}/acknowledge", (Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, IHubContext<FlowHub, IFlowHubClient> hub, HttpContext http, CancellationToken ct) =>
            AgentUpdate(id, n => n.Acknowledge(), dbf, tc, hub, http, ct));
        g.MapPost("{id:guid}/retract", Retract);
        return app;
    }

    private static Guid? Me(HttpContext http) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;

    internal static bool IsSupervisor(HttpContext http) =>
        (http.User.FindFirst("permissions")?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Any(p => p is Permission.SupervisorMonitor or Permission.SupervisorOverride);

    private static object Dto(CoachingNote n) => new
    {
        n.Id, n.AgentId, n.FromId, n.FromName, n.Text, n.CallRecordId, n.CreatedAt, n.SeenAt, n.AcknowledgedAt, n.RetractedAt, n.Status,
    };

    /// <summary>The agent's portal shows/removes the note; supervisors' composers refresh that agent's list.</summary>
    private static async Task NotifyAsync(IHubContext<FlowHub, IFlowHubClient> hub, Guid tenantId, CoachingNote n)
    {
        await hub.Clients.Group($"agent:{n.AgentId}").ReceiveCoachingNotesChanged();
        await hub.Clients.Group($"supervisor:{tenantId}").ReceiveCoachingNoteStatus(n.AgentId.ToString());
    }

    public record SendNoteRequest(Guid AgentId, string? Text);

    private static async Task<IResult> Send(SendNoteRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc, IFlowEngine flows,
        IHubContext<FlowHub, IFlowHubClient> hub, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (!IsSupervisor(http) || Me(http) is not { } me) return Results.Forbid();
        if (req.AgentId == me) return Results.BadRequest(new { error = "That's you." });
        await using var db = dbf.Create();
        if (!await db.Agents.AnyAsync(a => a.Id == req.AgentId && a.IsActive, ct)) return Results.BadRequest(new { error = "That agent wasn't found." });
        var from = await db.Agents.Where(a => a.Id == me).Select(a => (a.FirstName + " " + a.LastName).Trim()).FirstOrDefaultAsync(ct) ?? "Your supervisor";
        // The call they're on right now (their newest open script), so the coaching shows on that call's record.
        var call = (await flows.GetLiveSessionsForAgentsAsync([req.AgentId], ct)).OrderByDescending(s => s.StartedAt).FirstOrDefault()?.CallRecordId;
        CoachingNote note;
        try { note = CoachingNote.Create(tenant.Id, req.AgentId, me, from, req.Text ?? "", call); }
        catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        db.CoachingNotes.Add(note);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(hub, tenant.Id, note);
        return Results.Ok(Dto(note));
    }

    /// <summary>Supervisor: one agent's recent notes (newest first) with their status.</summary>
    private static async Task<IResult> ForAgent(Guid agentId, ScopedTenantDbContextFactory dbf, TenantContext tc, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is null) return Results.Unauthorized();
        if (!IsSupervisor(http)) return Results.Forbid();
        await using var db = dbf.Create();
        var since = DateTimeOffset.UtcNow.AddDays(-2);
        var notes = await db.CoachingNotes.AsNoTracking().Where(n => n.AgentId == agentId && n.CreatedAt > since)
            .OrderByDescending(n => n.CreatedAt).Take(20).ToListAsync(ct);
        return Results.Ok(notes.Select(Dto));
    }

    /// <summary>Agent: the notes still pinned in my portal (oldest first).</summary>
    private static async Task<IResult> Mine(ScopedTenantDbContextFactory dbf, TenantContext tc, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is null || Me(http) is not { } me) return Results.Unauthorized();
        await using var db = dbf.Create();
        var since = DateTimeOffset.UtcNow.AddHours(-12);
        var notes = await db.CoachingNotes.AsNoTracking()
            .Where(n => n.AgentId == me && n.AcknowledgedAt == null && n.RetractedAt == null && n.CreatedAt > since)
            .OrderBy(n => n.CreatedAt).ToListAsync(ct);
        return Results.Ok(notes.Select(Dto));
    }

    /// <summary>Coaching sent during a call — shown on its Call Records page.</summary>
    private static async Task<IResult> ForCall(Guid callRecordId, ScopedTenantDbContextFactory dbf, TenantContext tc, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is null) return Results.Unauthorized();
        var perms = (http.User.FindFirst("permissions")?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (!IsSupervisor(http) && !perms.Contains(Permission.CallsManage)) return Results.Forbid();
        await using var db = dbf.Create();
        var notes = await db.CoachingNotes.AsNoTracking().Where(n => n.CallRecordId == callRecordId).OrderBy(n => n.CreatedAt).ToListAsync(ct);
        return Results.Ok(notes.Select(Dto));
    }

    /// <summary>Agent: seen / got it — only on my own notes.</summary>
    private static async Task<IResult> AgentUpdate(Guid id, Action<CoachingNote> change, ScopedTenantDbContextFactory dbf, TenantContext tc,
        IHubContext<FlowHub, IFlowHubClient> hub, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant || Me(http) is not { } me) return Results.Unauthorized();
        await using var db = dbf.Create();
        var note = await db.CoachingNotes.FirstOrDefaultAsync(n => n.Id == id && n.AgentId == me, ct);
        if (note is null) return Results.NotFound();
        var before = note.Status;
        change(note);
        if (note.Status == before) return Results.Ok(Dto(note));
        await db.SaveChangesAsync(ct);
        await NotifyAsync(hub, tenant.Id, note);
        return Results.Ok(Dto(note));
    }

    /// <summary>Supervisor: take a note back (it disappears from the agent's portal).</summary>
    private static async Task<IResult> Retract(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, IHubContext<FlowHub, IFlowHubClient> hub,
        HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (!IsSupervisor(http)) return Results.Forbid();
        await using var db = dbf.Create();
        var note = await db.CoachingNotes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return Results.NotFound();
        note.Retract();
        await db.SaveChangesAsync(ct);
        await NotifyAsync(hub, tenant.Id, note);
        return Results.Ok(Dto(note));
    }
}
