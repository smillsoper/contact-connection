using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Remote fixes (S184): a supervisor runs diagnostics on an agent's portal, re-checks their extension, re-registers
/// their softphone, clears a stuck call screen or refreshes the page — without the agent doing anything. The request
/// is pushed to the agent's open portal, which does it and reports back; the supervisor sees the result live, the
/// agent sees a notice, and every action is kept (remote_actions).
/// </summary>
public static class RemoteActionsEndpoints
{
    public static IEndpointRouteBuilder MapRemoteActionsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/remote-actions").RequireAuthorization();
        g.MapPost("", Request);
        g.MapGet("", Recent);
        g.MapPost("{id:guid}/result", Result);
        return app;
    }

    private static string[] Perms(HttpContext http) => (http.User.FindFirst("permissions")?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
    private static bool CanLook(HttpContext http) => Perms(http).Any(p => p is Permission.SupervisorMonitor or Permission.SupervisorOverride);
    private static bool CanChange(HttpContext http) => Perms(http).Contains(Permission.SupervisorOverride);
    private static Guid? Me(HttpContext http) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;

    private static object Dto(RemoteAction a) => new
    {
        a.Id, a.AgentId, a.RequestedByName, a.Action, a.RequestedAt, a.CompletedAt, a.Ok, a.Detail,
    };

    public record RemoteActionRequest(Guid AgentId, string? Action, bool Force);

    private static async Task<IResult> Request(RemoteActionRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc,
        ITelephonyCallSessionStore calls, IHubContext<FlowHub, IFlowHubClient> hub, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is not { } tenant || Me(http) is not { } me) return Results.Unauthorized();
        var action = req.Action ?? "";
        if (!RemoteActionType.All.Contains(action)) return Results.BadRequest(new { error = "Unknown fix." });
        if (!CanLook(http) || (RemoteActionType.Disruptive(action) && !CanChange(http))) return Results.Forbid();
        if (req.AgentId == me) return Results.BadRequest(new { error = "That's your own portal — just do it there." });
        await using var db = dbf.Create();
        if (!await db.Agents.AnyAsync(a => a.Id == req.AgentId && a.IsActive, ct)) return Results.BadRequest(new { error = "That agent wasn't found." });

        // A refresh drops the softphone's call audio — refuse during a live call unless the supervisor confirmed.
        if (action == RemoteActionType.Refresh && !req.Force)
        {
            var onCall = (await calls.GetAllAsync(ct)).Any(s => s.TenantId == tenant.Id
                && s.Vars.GetValueOrDefault("_assigned_agent_id") == req.AgentId.ToString()
                && !string.IsNullOrEmpty(ContactConnection.Api.Telephony.SupervisorCallService.AgentLeg(s)));
            if (onCall) return Results.Conflict(new { error = "They're on a live call — refreshing would drop it.", onCall = true });
        }

        var name = await db.Agents.Where(a => a.Id == me).Select(a => (a.FirstName + " " + a.LastName).Trim()).FirstOrDefaultAsync(ct) ?? "Your supervisor";
        var row = RemoteAction.Create(tenant.Id, req.AgentId, me, name, action);
        db.RemoteActions.Add(row);
        await db.SaveChangesAsync(ct);
        await hub.Clients.Group($"agent:{req.AgentId}").ReceiveRemoteAction(row.Id.ToString(), action, name);
        return Results.Ok(Dto(row));
    }

    /// <summary>Supervisor: an agent's recent remote fixes (newest first).</summary>
    private static async Task<IResult> Recent(Guid agentId, ScopedTenantDbContextFactory dbf, TenantContext tc, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is null) return Results.Unauthorized();
        if (!CanLook(http)) return Results.Forbid();
        await using var db = dbf.Create();
        var since = DateTimeOffset.UtcNow.AddDays(-2);
        var rows = await db.RemoteActions.AsNoTracking().Where(a => a.AgentId == agentId && a.RequestedAt > since)
            .OrderByDescending(a => a.RequestedAt).Take(20).ToListAsync(ct);
        return Results.Ok(rows.Select(Dto));
    }

    public record RemoteActionResult(bool Ok, string? Detail);

    /// <summary>Agent portal: what happened. Only the agent the fix was for can report it.</summary>
    private static async Task<IResult> Result(Guid id, RemoteActionResult req, ScopedTenantDbContextFactory dbf, TenantContext tc,
        IHubContext<FlowHub, IFlowHubClient> hub, HttpContext http, CancellationToken ct)
    {
        if (tc.Current is null || Me(http) is not { } me) return Results.Unauthorized();
        await using var db = dbf.Create();
        var row = await db.RemoteActions.FirstOrDefaultAsync(a => a.Id == id && a.AgentId == me, ct);
        if (row is null) return Results.NotFound();
        row.Complete(req.Ok, req.Detail);
        await db.SaveChangesAsync(ct);
        await hub.Clients.Group($"agent:{row.RequestedById}").ReceiveRemoteActionResult(row.Id.ToString(), row.Ok == true, row.Detail);
        return Results.Ok(Dto(row));
    }
}
