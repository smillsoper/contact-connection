using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Supervisor tools on an agent's live call (S167) — see SupervisorCallService. Monitor and Coach
/// need supervisor.monitor; Barge and Take Over need supervisor.override.
/// </summary>
public static class SupervisorEndpoints
{
    public static IEndpointRouteBuilder MapSupervisorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/supervisor").RequireAuthorization();
        group.MapGet("me", GetMe);
        group.MapGet("monitor", GetMonitor);
        group.MapPost("monitor", StartMonitor);
        group.MapPut("monitor", SetMode);
        group.MapDelete("monitor", StopMonitor);
        group.MapPost("take-over", TakeOver);
        group.MapPost("call-agent", CallAgent);
        return app;
    }

    // Whether the supervisor's own softphone (an open agent portal) is registered right now — the
    // dashboard takes a call over into that portal directly instead of opening another one.
    private static async Task<IResult> GetMe(HttpContext http, IAgentRepository agents,
        ContactConnection.Application.Interfaces.Services.IAgentRegistrationStore registrations,
        TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant || !TryActor(http, out var supervisorId)) return Results.Unauthorized();
        var me = await agents.GetByIdAsync(supervisorId, ct);
        var registered = me?.SipExtension is { Length: > 0 } ext
            && registrations.Get(tenantContext.Current!.Id, ext) is not null;
        return Results.Ok(new { agentId = supervisorId, registered });
    }

    private static async Task<IResult> GetMonitor(HttpContext http, SupervisorCallService svc, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant || !TryActor(http, out var supervisorId)) return Results.Unauthorized();
        var state = await svc.GetMonitorAsync(supervisorId, ct);
        return state is null ? Results.NoContent() : Results.Ok(state);
    }

    private static async Task<IResult> StartMonitor(StartMonitorRequest req, HttpContext http, SupervisorCallService svc,
        IAgentRepository agents, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant || !TryActor(http, out var supervisorId)) return Results.Unauthorized();
        var mode = req.Mode ?? MonitorMode.Listen;
        if (!Allowed(http, mode)) return Results.Forbid();
        var supervisor = await agents.GetByIdAsync(supervisorId, ct);
        if (supervisor is null) return Results.Unauthorized();
        return ToResult(await svc.StartMonitorAsync(supervisor, req.AgentId, mode, ct));
    }

    private static async Task<IResult> SetMode(SetMonitorModeRequest req, HttpContext http, SupervisorCallService svc,
        IAgentRepository agents, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant || !TryActor(http, out var supervisorId)) return Results.Unauthorized();
        if (!Allowed(http, req.Mode)) return Results.Forbid();
        var supervisor = await agents.GetByIdAsync(supervisorId, ct);
        if (supervisor is null) return Results.Unauthorized();
        return ToResult(await svc.SetMonitorModeAsync(supervisor, req.Mode, ct));
    }

    private static async Task<IResult> StopMonitor(HttpContext http, SupervisorCallService svc, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant || !TryActor(http, out var supervisorId)) return Results.Unauthorized();
        await svc.StopMonitorAsync(supervisorId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TakeOver(TakeOverRequest req, HttpContext http, SupervisorCallService svc,
        IAgentRepository agents, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant || !TryActor(http, out var supervisorId)) return Results.Unauthorized();
        if (!HasPermission(http, Permission.SupervisorOverride)) return Results.Forbid();
        var supervisor = await agents.GetByIdAsync(supervisorId, ct);
        if (supervisor is null) return Results.Unauthorized();
        return ToResult(await svc.TakeOverAsync(supervisor, req.AgentId, ct));
    }

    // Supervisor → agent internal call (QA review, training) — either supervisor permission.
    private static async Task<IResult> CallAgent(TakeOverRequest req, HttpContext http, SupervisorCallService svc,
        IAgentRepository agents, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant || !TryActor(http, out var supervisorId)) return Results.Unauthorized();
        if (!Allowed(http, MonitorMode.Listen)) return Results.Forbid();
        var supervisor = await agents.GetByIdAsync(supervisorId, ct);
        if (supervisor is null) return Results.Unauthorized();
        return ToResult(await svc.CallAgentAsync(supervisor, req.AgentId, ct));
    }

    // Listen / coach = supervisor.monitor; barge (the caller hears you) = supervisor.override.
    private static bool Allowed(HttpContext http, string mode) => mode == MonitorMode.Barge
        ? HasPermission(http, Permission.SupervisorOverride)
        : HasPermission(http, Permission.SupervisorMonitor) || HasPermission(http, Permission.SupervisorOverride);

    private static bool HasPermission(HttpContext http, string permission) =>
        (http.User.FindFirst("permissions")?.Value ?? "").Split(',').Contains(permission);

    private static bool TryActor(HttpContext http, out Guid id) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out id);

    private static IResult ToResult(SupervisorResult r) =>
        r.Ok ? Results.Ok(r.Data) : Results.Conflict(new { error = r.Error });
}

public record StartMonitorRequest(Guid AgentId, string? Mode);
public record SetMonitorModeRequest(string Mode);
public record TakeOverRequest(Guid AgentId);
