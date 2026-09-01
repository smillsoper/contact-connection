using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Voicemail inbox + playback. Rows are written by the ESL background path when a tf_voicemail
/// node captures a caller message; the audio lives in <see cref="IBlobStorage"/> under the row's
/// storage key. Supervisor dashboards also get a live <c>ReceiveVoicemail</c> SignalR push at
/// capture time.
/// </summary>
public static class VoicemailsEndpoints
{
    public static IEndpointRouteBuilder MapVoicemailsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/voicemails").RequireAuthorization();
        group.MapGet("/{id:guid}", GetById);
        group.MapGet("/{id:guid}/audio", GetAudio);
        group.MapPost("/{id:guid}/heard", MarkHeard);
        group.MapPost("/{id:guid}/archive", Archive);
        group.MapPost("/{id:guid}/restore", Restore);

        app.MapGet("/api/v1/call-records/{callRecordId:guid}/voicemails", ListForCall)
           .RequireAuthorization();
        app.MapGet("/api/v1/campaigns/{campaignId:guid}/voicemails", ListForCampaign)
           .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ListForCall(
        Guid callRecordId, IVoicemailRepository repo, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        var list = await repo.ListByCallRecordAsync(callRecordId, ct);
        return Results.Ok(list.Select(ToResponse));
    }

    private static async Task<IResult> ListForCampaign(
        Guid campaignId, string? status, int? limit,
        IVoicemailRepository repo, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        if (status is not null && !VoicemailStatus.IsValid(status))
            return Results.BadRequest(new { error = $"invalid status '{status}'." });

        var list = await repo.ListByCampaignAsync(campaignId, status, Math.Clamp(limit ?? 100, 1, 500), ct);
        return Results.Ok(list.Select(ToResponse));
    }

    private static async Task<IResult> GetById(
        Guid id, IVoicemailRepository repo, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        var vm = await repo.GetByIdAsync(id, ct);
        return vm is null ? Results.NotFound() : Results.Ok(ToResponse(vm));
    }

    private static async Task<IResult> GetAudio(
        Guid id, IVoicemailRepository repo, IBlobStorage blobs, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        var vm = await repo.GetByIdAsync(id, ct);
        if (vm is null) return Results.NotFound();

        var stream = await blobs.OpenReadAsync(vm.StorageKey, ct);
        if (stream is null) return Results.Json(new { status = "missing" }, statusCode: StatusCodes.Status404NotFound);

        return Results.Stream(
            stream, contentType: "audio/wav", fileDownloadName: null,
            lastModified: vm.CreatedAt, entityTag: null, enableRangeProcessing: true);
    }

    private static async Task<IResult> MarkHeard(
        Guid id, System.Security.Claims.ClaimsPrincipal user,
        IVoicemailRepository repo, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var agentId)) return Results.Unauthorized();

        var vm = await repo.GetByIdAsync(id, ct);
        if (vm is null) return Results.NotFound();

        vm.MarkHeard(agentId);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(vm));
    }

    private static async Task<IResult> Archive(
        Guid id, IVoicemailRepository repo, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        var vm = await repo.GetByIdAsync(id, ct);
        if (vm is null) return Results.NotFound();

        vm.Archive();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(vm));
    }

    private static async Task<IResult> Restore(
        Guid id, IVoicemailRepository repo, TenantContext tenant, CancellationToken ct)
    {
        if (tenant.Current is null) return Results.Unauthorized();
        var vm = await repo.GetByIdAsync(id, ct);
        if (vm is null) return Results.NotFound();

        vm.Restore();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(vm));
    }

    private static object ToResponse(Voicemail v) => new
    {
        id                  = v.Id,
        callRecordId        = v.CallRecordId,
        campaignId          = v.CampaignId,
        callerId            = v.CallerId,
        durationSeconds     = v.DurationSeconds,
        status              = v.Status,
        transcription       = v.Transcription,
        emailDeliveryStatus = v.EmailDeliveryStatus,
        emailDeliveredTo    = v.EmailDeliveredTo,
        emailDeliveryError  = v.EmailDeliveryError,
        emailDeliveredAt    = v.EmailDeliveredAt,
        createdAt           = v.CreatedAt,
        heardAt             = v.HeardAt,
        heardBy             = v.HeardBy,
        archivedAt          = v.ArchivedAt,
        audioUrl            = $"/api/v1/voicemails/{v.Id}/audio",
    };
}
