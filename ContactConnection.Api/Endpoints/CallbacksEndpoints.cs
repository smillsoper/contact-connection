using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Callback list + supervisor cancel. Rows are created by a <c>tf_request_callback</c> telephony
/// node when a queued caller opts out of holding; the outbound leg is placed by the Worker's
/// <c>CallbackProcessingService</c> when the window opens. See ARCHITECTURE.md §16.
/// </summary>
public static class CallbacksEndpoints
{
    public static IEndpointRouteBuilder MapCallbacksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/callbacks/{id:guid}", GetById).RequireAuthorization();
        app.MapPost("/api/v1/callbacks/{id:guid}/cancel", Cancel).RequireAuthorization();
        app.MapGet("/api/v1/call-records/{callRecordId:guid}/callbacks", ListForCall).RequireAuthorization();
        app.MapGet("/api/v1/campaigns/{campaignId:guid}/callbacks", ListForCampaign).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> GetById(
        Guid id, ICallbackRepository repo, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        var cb = await repo.GetByIdAsync(id, ct);
        return cb is null ? Results.NotFound() : Results.Ok(ToResponse(cb));
    }

    private static async Task<IResult> ListForCall(
        Guid callRecordId, ICallbackRepository repo, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        var list = await repo.ListByCallRecordAsync(callRecordId, ct);
        return Results.Ok(list.Select(ToResponse));
    }

    private static async Task<IResult> ListForCampaign(
        Guid campaignId, string? status, int? limit,
        ICallbackRepository repo, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        if (status is not null && !CallbackStatus.IsValid(status))
            return Results.BadRequest(new { error = $"invalid status '{status}'." });

        var list = await repo.ListByCampaignAsync(campaignId, status, Math.Clamp(limit ?? 100, 1, 500), ct);
        return Results.Ok(list.Select(ToResponse));
    }

    private static async Task<IResult> Cancel(
        Guid id, CancelCallbackRequest? body,
        ICallbackRepository repo, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        var cb = await repo.GetByIdAsync(id, ct);
        if (cb is null) return Results.NotFound();

        if (CallbackStatus.IsTerminal(cb.Status))
            return Results.Conflict(new { error = $"callback is already '{cb.Status}'." });
        if (cb.Status == CallbackStatus.Attempted)
            return Results.Conflict(new { error = "callback is mid-attempt and cannot be cancelled." });

        cb.Cancel(string.IsNullOrWhiteSpace(body?.Reason) ? "Cancelled by supervisor." : body!.Reason!.Trim());
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(cb));
    }

    private static object ToResponse(Callback c) => new
    {
        id                   = c.Id,
        callRecordId         = c.CallRecordId,
        campaignId           = c.CampaignId,
        callbackNumber       = c.CallbackNumber,
        status               = c.Status,
        requestedAt          = c.RequestedAt,
        scheduledFor         = c.ScheduledFor,
        expiresAt            = c.ExpiresAt,
        attemptCount         = c.AttemptCount,
        maxAttempts          = c.MaxAttempts,
        lastAttemptAt        = c.LastAttemptAt,
        outboundCallRecordId = c.OutboundCallRecordId,
        completedAt          = c.CompletedAt,
        abandonedAt          = c.AbandonedAt,
        expiredAt            = c.ExpiredAt,
        cancelledAt          = c.CancelledAt,
        detail               = c.Detail,
        createdAt            = c.CreatedAt,
        updatedAt            = c.UpdatedAt,
    };
}

public record CancelCallbackRequest(string? Reason);
