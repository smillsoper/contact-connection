using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Playback for the merged call recording produced by the <c>RecordingMergeService</c> worker.
/// <c>CallRecord.RecordingUrl</c> points here once a merge job completes; the bytes live in
/// <see cref="IBlobStorage"/> under the job's output key. Range requests are enabled so the
/// (future) sync player can seek.
/// </summary>
public static class CallRecordingsEndpoints
{
    public static IEndpointRouteBuilder MapCallRecordingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/call-records/{id:guid}/recording", GetRecording)
           .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetRecording(
        Guid id,
        ICallRecordRepository callRecords,
        IRecordingMergeJobRepository mergeJobs,
        IBlobStorage blobs,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();

        var record = await callRecords.GetByIdAsync(id, ct);
        if (record is null) return Results.NotFound();

        if (!record.RecordingRetained)
            return Results.Json(
                new { status = "purged", reason = record.RecordingDeleteReason },
                statusCode: StatusCodes.Status410Gone);

        var job = await mergeJobs.GetByCallRecordIdAsync(id, ct);

        if (job is null || job.Status is RecordingMergeJobStatus.Failed or RecordingMergeJobStatus.Skipped)
            return Results.Json(new { status = job?.Status ?? "none" }, statusCode: StatusCodes.Status404NotFound);

        if (job.Status is RecordingMergeJobStatus.Pending or RecordingMergeJobStatus.Processing
            || string.IsNullOrEmpty(job.OutputBlobKey))
            return Results.Json(new { status = "processing" }, statusCode: StatusCodes.Status202Accepted);

        var stream = await blobs.OpenReadAsync(job.OutputBlobKey, ct);
        if (stream is null)
            return Results.Json(new { status = "missing" }, statusCode: StatusCodes.Status404NotFound);

        var contentType = job.OutputFormat == "mp4" ? "video/mp4" : "audio/mp4";
        return Results.Stream(
            stream,
            contentType: contentType,
            fileDownloadName: null,
            lastModified: job.CompletedAt,
            entityTag: null,
            enableRangeProcessing: true);
    }
}
