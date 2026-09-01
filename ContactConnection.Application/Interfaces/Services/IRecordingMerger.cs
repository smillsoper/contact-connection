namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Transcodes a call's stereo audio recording and, when a usable agent screen capture exists,
/// muxes the two into a single playable artifact stored in <see cref="IBlobStorage"/>. Pure
/// media work — the caller (the <c>RecordingMergeService</c> worker) owns job state and the
/// call-record write. Implemented over the <c>ffmpeg</c> CLI.
/// </summary>
public interface IRecordingMerger
{
    Task<RecordingMergeResult> MergeAsync(RecordingMergeRequest request, CancellationToken ct = default);
}

/// <summary>One screen capture segment available for a call (see <c>ScreenRecording</c>).</summary>
public sealed record ScreenRecordingInput(
    Guid Id,
    string StorageKey,
    string Container,
    IReadOnlyList<int> ChunkIndices,
    DateTimeOffset StartedAtServer,
    long? DurationMs);

/// <param name="AudioSourcePath">Absolute path (on the worker host) to the stereo call-audio WAV written by uuid_record.</param>
/// <param name="RecordingStartedAt">Server-clock instant the audio recording started — the master time base for alignment.</param>
/// <param name="OutputBlobPrefix">Blob key prefix for output, e.g. "recordings". The merger appends "/{callRecordId}/merged.{ext}".</param>
public sealed record RecordingMergeRequest(
    Guid CallRecordId,
    string AudioSourcePath,
    DateTimeOffset RecordingStartedAt,
    IReadOnlyList<ScreenRecordingInput> ScreenRecordings,
    string OutputBlobPrefix);

public sealed record RecordingMergeResult(
    bool Success,
    string? OutputBlobKey,
    string? OutputFormat,        // "mp4" | "m4a"
    long? OutputDurationMs,
    bool HadVideo,
    Guid? ScreenRecordingId,
    int ScreenRecordingCount,
    string FfmpegCommand,
    string? Error)
{
    public static RecordingMergeResult Ok(
        string outputBlobKey, string outputFormat, long? durationMs, bool hadVideo,
        Guid? screenRecordingId, int screenRecordingCount, string ffmpegCommand) =>
        new(true, outputBlobKey, outputFormat, durationMs, hadVideo,
            screenRecordingId, screenRecordingCount, ffmpegCommand, null);

    public static RecordingMergeResult Fail(string error, string ffmpegCommand = "") =>
        new(false, null, null, null, false, null, 0, ffmpegCommand, error);
}
