using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class CallTracesEndpoints
{
    public static IEndpointRouteBuilder MapCallTracesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/call-traces").RequireAuthorization();

        group.MapPost("", StartTrace);
        group.MapPost("{subscriptionId:guid}/stop", StopTrace);
        group.MapGet("{callRecordId:guid}", GetTimeline);
        group.MapGet("search", Search);

        return app;
    }

    // ── POST /api/v1/call-traces ────────────────────────────────────────────
    // Clamps the requested capture value to the server ceiling regardless of what was asked —
    // the hard backstop against a runaway trace. Returns the effective (possibly clamped) cap.

    private static async Task<IResult> StartTrace(
        StartTraceApiRequest request,
        ICallTraceSubscriptionRegistry registry,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        if (request.CaptureMode != CallTraceCaptureMode.Count && request.CaptureMode != CallTraceCaptureMode.Duration)
            return Results.BadRequest(new { Message = "captureMode must be 'count' or 'duration'." });

        var effectiveCaptureValue = request.CaptureMode == CallTraceCaptureMode.Duration
            ? Math.Clamp(request.CaptureValue, 1, (int)CallTraceLimits.MaxCaptureDuration.TotalMinutes)
            : Math.Clamp(request.CaptureValue, 1, CallTraceLimits.MaxCaptureCount);

        var subscriptionId = await registry.StartTraceAsync(new StartTraceRequest
        {
            TenantId = tenantContext.Current!.Id,
            CampaignId = request.CampaignId,
            FlowId = request.FlowId,
            Dnis = string.IsNullOrWhiteSpace(request.Dnis) ? null : request.Dnis,
            Ani = string.IsNullOrWhiteSpace(request.Ani) ? null : request.Ani,
            CaptureMode = request.CaptureMode,
            CaptureValue = effectiveCaptureValue,
        }, ct);

        return Results.Ok(new
        {
            subscriptionId,
            effectiveCaptureMode = request.CaptureMode,
            effectiveCaptureValue,
        });
    }

    // ── POST /api/v1/call-traces/{subscriptionId}/stop ──────────────────────

    private static async Task<IResult> StopTrace(
        Guid subscriptionId,
        ICallTraceSubscriptionRegistry registry,
        CancellationToken ct)
    {
        await registry.StopTraceAsync(subscriptionId, "closed by user", ct);
        return Results.NoContent();
    }

    // ── GET /api/v1/call-traces/{callRecordId} ──────────────────────────────
    // Full ordered timeline for one call — durable, independent of any live popup.

    private static async Task<IResult> GetTimeline(
        Guid callRecordId,
        ICallTraceEventRepository repository,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var steps = await repository.GetByCallRecordIdAsync(callRecordId, tenantContext.Current!.SchemaName, ct);
        if (steps.Count == 0 || steps[0].TenantId != tenantContext.Current!.Id)
            return Results.NotFound();

        return Results.Ok(steps.Select(ToResponse));
    }

    // ── GET /api/v1/call-traces/search ───────────────────────────────────────
    // Browse past calls matching a filter, for reopening/replaying one later.

    private static async Task<IResult> Search(
        Guid? campaignId,
        Guid? flowId,
        string? dnis,
        string? ani,
        int? limit,
        ICallTraceEventRepository repository,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var results = await repository.SearchCallsAsync(
            tenantContext.Current!.Id, tenantContext.Current.SchemaName, campaignId, flowId, dnis, ani, limit ?? 50, ct);

        return Results.Ok(results);
    }

    private static object ToResponse(CallTraceEvent e) => new
    {
        e.CallRecordId,
        e.Sequence,
        e.Engine,
        e.NodeId,
        e.NodeType,
        e.Label,
        e.EnteredAt,
        e.Detail,
        e.TransitionTaken,
        e.ExitReason,
        e.NextNodeId,
    };
}

public record StartTraceApiRequest(
    Guid? CampaignId,
    Guid? FlowId,
    string? Dnis,
    string? Ani,
    string CaptureMode,
    int CaptureValue);
