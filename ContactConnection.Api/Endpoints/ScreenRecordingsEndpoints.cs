using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Ingest for agent-side screen captures from the browser extension. Chunks stream in over HTTP
/// and land in <see cref="IBlobStorage"/>; the <see cref="ScreenRecording"/> row tracks progress,
/// the client↔server clock relationship, and the sync cue points the future A/V merge aligns to.
/// The extension itself is not built yet — this is the server contract it will target.
/// </summary>
public static class ScreenRecordingsEndpoints
{
    public static void MapScreenRecordingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/screen-recordings").RequireAuthorization();

        // Bare server time — the extension polls this to keep its clock offset fresh.
        group.MapGet("/time", () => Results.Ok(new { serverTime = DateTimeOffset.UtcNow }));

        group.MapPost("/", StartRecording);
        group.MapPut("/{id:guid}/chunks/{index:int}", UploadChunk)
             .DisableAntiforgery();   // raw binary body, not a form
        group.MapPost("/{id:guid}/cuepoints", AddCuePoints);
        group.MapPost("/{id:guid}/complete", CompleteRecording);
        group.MapPost("/{id:guid}/abort", AbortRecording);
        group.MapGet("/{id:guid}", GetRecording);

        // List every screen capture for a call (warm transfer → one per answering agent).
        app.MapGet("/api/v1/call-records/{callRecordId:guid}/screen-recordings", ListForCall)
           .RequireAuthorization();
    }

    private static async Task<IResult> StartRecording(
        StartScreenRecordingRequest req,
        IScreenRecordingRepository repo,
        TenantContext tenantContext,
        HttpContext http,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();
        if (!TryGetAgentId(http, out var agentId)) return Results.Unauthorized();
        if (req.CallRecordId == Guid.Empty) return Results.BadRequest(new { error = "callRecordId is required." });

        var now = DateTimeOffset.UtcNow;
        var recording = ScreenRecording.Create(
            tenantId:        tenantContext.Current.Id,
            callRecordId:    req.CallRecordId,
            interactionId:   req.InteractionId,
            agentId:         agentId,
            container:       req.Container ?? "webm",
            codec:           req.Codec ?? "",
            startedAtClient: req.StartedAtClient,
            serverNow:       now);

        await repo.AddAsync(recording, ct);
        await repo.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/screen-recordings/{recording.Id}", recording.ToResponse());
    }

    private static async Task<IResult> UploadChunk(
        Guid id, int index,
        IScreenRecordingRepository repo,
        IBlobStorage blobs,
        TenantContext tenantContext,
        HttpContext http,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();
        if (index < 0) return Results.BadRequest(new { error = "chunk index must be >= 0." });

        var recording = await repo.GetByIdAsync(id, ct);
        if (recording is null || recording.TenantId != tenantContext.Current.Id) return Results.NotFound();
        if (recording.Status is ScreenRecordingStatus.Complete or ScreenRecordingStatus.Aborted)
            return Results.Conflict(new { error = $"screen recording is {recording.Status}." });

        // Buffer the chunk so we know its length even under chunked transfer-encoding.
        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer, ct);
        if (buffer.Length == 0) return Results.BadRequest(new { error = "empty chunk body." });
        buffer.Position = 0;

        var ext = recording.Container == "mp4" ? "mp4" : "webm";
        var contentType = recording.Container == "mp4" ? "video/mp4" : "video/webm";
        await blobs.PutAsync($"{recording.StorageKey}/{index:D6}.{ext}", buffer, contentType, ct);

        recording.RegisterChunk(index, buffer.Length);
        await repo.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            received = recording.ReceivedChunkIndices,
            chunkCount = recording.ChunkCount,
            totalBytes = recording.TotalBytes,
        });
    }

    private static async Task<IResult> AddCuePoints(
        Guid id, AddCuePointsRequest req,
        IScreenRecordingRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();
        var recording = await repo.GetByIdAsync(id, ct);
        if (recording is null || recording.TenantId != tenantContext.Current.Id) return Results.NotFound();

        foreach (var p in req.Points ?? [])
            recording.AddCuePoint(p.AtMs, p.Kind ?? "custom", p.Detail);

        await repo.SaveChangesAsync(ct);
        return Results.Ok(recording.ToResponse());
    }

    private static async Task<IResult> CompleteRecording(
        Guid id, CompleteScreenRecordingRequest req,
        IScreenRecordingRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();
        var recording = await repo.GetByIdAsync(id, ct);
        if (recording is null || recording.TenantId != tenantContext.Current.Id) return Results.NotFound();

        try
        {
            recording.MarkComplete(req.DurationMs, req.Sha256);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message, received = recording.ReceivedChunkIndices });
        }

        await repo.SaveChangesAsync(ct);
        return Results.Ok(recording.ToResponse());
    }

    private static async Task<IResult> AbortRecording(
        Guid id,
        IScreenRecordingRepository repo,
        IBlobStorage blobs,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();
        var recording = await repo.GetByIdAsync(id, ct);
        if (recording is null || recording.TenantId != tenantContext.Current.Id) return Results.NotFound();

        recording.Abort();
        await repo.SaveChangesAsync(ct);
        await blobs.DeletePrefixAsync(recording.StorageKey, ct);
        return Results.Ok(recording.ToResponse());
    }

    private static async Task<IResult> GetRecording(
        Guid id,
        IScreenRecordingRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();
        var recording = await repo.GetByIdAsync(id, ct);
        return recording is null || recording.TenantId != tenantContext.Current.Id
            ? Results.NotFound()
            : Results.Ok(recording.ToResponse());
    }

    private static async Task<IResult> ListForCall(
        Guid callRecordId,
        IScreenRecordingRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();
        var list = await repo.ListByCallRecordAsync(callRecordId, ct);
        return Results.Ok(list.Select(r => r.ToResponse()));
    }

    private static bool TryGetAgentId(HttpContext http, out Guid agentId) =>
        Guid.TryParse(http.User.FindFirst("sub")?.Value, out agentId);

    private static object ToResponse(this ScreenRecording r) => new
    {
        id                  = r.Id,
        callRecordId        = r.CallRecordId,
        interactionId       = r.InteractionId,
        agentId             = r.AgentId,
        status              = r.Status,
        container           = r.Container,
        codec               = r.Codec,
        startedAtServer     = r.StartedAtServer,
        startedAtClient     = r.StartedAtClient,
        clientClockOffsetMs = r.ClientClockOffsetMs,
        storageKey          = r.StorageKey,
        receivedChunkIndices = r.ReceivedChunkIndices,
        chunkCount          = r.ChunkCount,
        totalBytes          = r.TotalBytes,
        durationMs          = r.DurationMs,
        sha256              = r.Sha256,
        cuePoints           = r.CuePoints.Select(c => new { atMs = c.AtMs, kind = c.Kind, detail = c.Detail }),
        failureReason       = r.FailureReason,
        completedAt         = r.CompletedAt,
        createdAt           = r.CreatedAt,
        updatedAt           = r.UpdatedAt,
    };
}

public record StartScreenRecordingRequest(
    Guid CallRecordId,
    Guid? InteractionId,
    DateTimeOffset StartedAtClient,
    string? Container = "webm",
    string? Codec = "");

public record AddCuePointsRequest(List<CuePointDto>? Points);
public record CuePointDto(long AtMs, string? Kind, string? Detail);

public record CompleteScreenRecordingRequest(long DurationMs, string? Sha256);
