using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Supervisor lock on an agent (S166) — set from Call Records "Finalize" (relieving or terminating
/// an agent), lifted from Users or the supervisor dashboard's Agent List.
///   status lock  — held Unavailable (AgentStateStore enforces it against every status change),
///                  picker disabled on their screen;
///   sign-in lock — also signed out now, existing tokens rejected (JWT OnTokenValidated), sign-in
///                  refused with "contact your supervisor".
/// Lock/unlock need agents.manage, calls.manage or supervisor.override.
/// </summary>
public static class AgentLockEndpoints
{
    public const string SignInLockedMessage = "Your login has been locked — please contact your supervisor.";

    public static IEndpointRouteBuilder MapAgentLockEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agents").RequireAuthorization();
        group.MapPost("{id:guid}/lock", Lock);
        group.MapPost("{id:guid}/unlock", Unlock);
        return app;
    }

    private static async Task<IResult> Lock(Guid id, LockAgentRequest req, HttpContext http, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanLock(http, out var actor)) return Results.Forbid();
        var agent = await LockAsync(http, id, req.SignIn, req.Reason, actor, ct);
        return agent is null ? Results.NotFound() : Results.Ok(ToResponse(agent));
    }

    private static async Task<IResult> Unlock(Guid id, HttpContext http, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (!CanLock(http, out var actor)) return Results.Forbid();
        var agent = await UnlockAsync(http, id, actor, ct);
        return agent is null ? Results.NotFound() : Results.Ok(ToResponse(agent));
    }

    /// <summary>Locks the agent and tells their screen (and, for a sign-in lock, signs them out).</summary>
    internal static async Task<Agent?> LockAsync(
        HttpContext http, Guid agentId, bool signIn, string? reason, (Guid Id, string Name) actor, CancellationToken ct)
    {
        var services = http.RequestServices;
        var agents = services.GetRequiredService<IAgentRepository>();
        var tenant = services.GetRequiredService<TenantContext>().Current!;
        var agent = await agents.GetByIdAsync(agentId, ct);
        if (agent is null) return null;

        agent.Lock(actor.Name, reason, signIn);
        await agents.SaveChangesAsync(ct);
        services.GetRequiredService<IAgentLockReader>().Invalidate(agentId);

        var message = $"Locked by {actor.Name}" + (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}");
        var notifier = services.GetRequiredService<IFlowNotifier>();
        var states = services.GetRequiredService<IAgentStateStore>();
        if (agent.SignInLocked)
        {
            await notifier.PushForceSignOutAsync(agentId, SignInLockedMessage, ct);
            await states.SetAsync(tenant.Id, agentId, tenant.SchemaName,
                new AgentStateEntry(AgentStateCodes.LoggedOut, "Logged Out", null, DateTimeOffset.UtcNow), ct);
        }
        else
        {
            await notifier.PushAgentLockChangedAsync(agentId, true, message, ct);
            // The store holds a locked agent at Unavailable whatever is set.
            await states.SetAsync(tenant.Id, agentId, tenant.SchemaName,
                new AgentStateEntry(AgentStateCodes.Unavailable, "Unavailable", null, DateTimeOffset.UtcNow), ct);
        }
        return agent;
    }

    internal static async Task<Agent?> UnlockAsync(HttpContext http, Guid agentId, (Guid Id, string Name) actor, CancellationToken ct)
    {
        var services = http.RequestServices;
        var agents = services.GetRequiredService<IAgentRepository>();
        var tenant = services.GetRequiredService<TenantContext>().Current!;
        var agent = await agents.GetByIdAsync(agentId, ct);
        if (agent is null) return null;

        agent.Unlock();
        await agents.SaveChangesAsync(ct);
        services.GetRequiredService<IAgentLockReader>().Invalidate(agentId);

        // Back to plain Unavailable (still signed in) — the agent chooses when to take calls again.
        var states = services.GetRequiredService<IAgentStateStore>();
        var current = await states.GetAsync(tenant.Id, agentId, ct);
        if (current is not null && current.Code != AgentStateCodes.LoggedOut)
            await states.SetAsync(tenant.Id, agentId, tenant.SchemaName,
                new AgentStateEntry(AgentStateCodes.Unavailable, "Unavailable", null, DateTimeOffset.UtcNow), ct);
        await services.GetRequiredService<IFlowNotifier>().PushAgentLockChangedAsync(agentId, false, $"Unlocked by {actor.Name}", ct);
        return agent;
    }

    private static bool CanLock(HttpContext http, out (Guid Id, string Name) actor)
    {
        actor = ActorResolver.Resolve(http.User) ?? (Guid.Empty, "Unknown");
        var permissions = (http.User.FindFirst("permissions")?.Value ?? "").Split(',');
        return actor.Id != Guid.Empty
            && (permissions.Contains(Permission.AgentsManage) || permissions.Contains(Permission.CallsManage)
                || permissions.Contains(Permission.SupervisorOverride));
    }

    internal static object ToResponse(Agent a) => new
    {
        a.Id,
        statusLocked = a.IsStatusLocked,
        a.SignInLocked,
        a.StatusLockedAt,
        a.StatusLockedByName,
        a.StatusLockReason,
    };
}

public record LockAgentRequest(bool SignIn, string? Reason);
