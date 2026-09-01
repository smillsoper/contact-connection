using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.Recording;

/// <summary>
/// <see cref="IRecordingMerger"/> over the ffmpeg CLI. Pulls the screen-capture chunks out of
/// blob storage, concatenates them, transcodes/muxes with the call audio, and writes the single
/// merged artifact back to blob storage. All scratch work happens in a per-call temp directory
/// that is always cleaned up.
/// </summary>
public sealed class FfmpegRecordingMerger : IRecordingMerger
{
    private readonly IFfmpegRunner _runner;
    private readonly IBlobStorage _blobs;
    private readonly ILogger<FfmpegRecordingMerger> _logger;

    public FfmpegRecordingMerger(IFfmpegRunner runner, IBlobStorage blobs, ILogger<FfmpegRecordingMerger> logger)
    {
        _runner = runner;
        _blobs  = blobs;
        _logger = logger;
    }

    public async Task<RecordingMergeResult> MergeAsync(RecordingMergeRequest request, CancellationToken ct = default)
    {
        if (!File.Exists(request.AudioSourcePath))
            return RecordingMergeResult.Fail($"audio source not found: {request.AudioSourcePath}");

        var workDir = Path.Combine(Path.GetTempPath(), "cc-recording-merge", request.CallRecordId.ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            var audioLocal = Path.Combine(workDir, "audio.wav");
            File.Copy(request.AudioSourcePath, audioLocal, overwrite: true);

            // ── pick a screen capture for the video track ────────────────────
            var chosen = SelectScreenRecording(request.ScreenRecordings);
            string? screenLocal = null;
            var hadVideo = false;

            if (chosen is not null)
            {
                screenLocal = Path.Combine(workDir, $"screen.{ChunkExtension(chosen.Container)}");
                if (await TryAssembleScreenAsync(chosen, screenLocal, ct))
                    hadVideo = true;
                else
                    _logger.LogWarning(
                        "Screen capture {ScreenId} for call {CallId} could not be assembled — producing audio-only output",
                        chosen.Id, request.CallRecordId);
            }

            // ── build + run ffmpeg ──────────────────────────────────────────
            var ext = hadVideo ? "mp4" : "m4a";
            var outLocal = Path.Combine(workDir, $"merged.{ext}");

            IReadOnlyList<string> args;
            if (hadVideo)
            {
                var offset = (chosen!.StartedAtServer - request.RecordingStartedAt).TotalSeconds;
                args = FfmpegCommandBuilder.BuildMux(audioLocal, screenLocal!, offset, outLocal);
            }
            else
            {
                args = FfmpegCommandBuilder.BuildAudioOnly(audioLocal, outLocal);
            }

            var command = "ffmpeg " + string.Join(' ', args);
            var run = await _runner.RunAsync(args, ct);

            // If the mux failed, fall back to audio-only so the call still gets a recording.
            if (!run.Success && hadVideo)
            {
                _logger.LogWarning(
                    "Mux ffmpeg run failed for call {CallId} (exit {Exit}) — retrying audio-only. stderr: {Err}",
                    request.CallRecordId, run.ExitCode, Tail(run.StdErr));

                hadVideo = false;
                ext = "m4a";
                outLocal = Path.Combine(workDir, "merged.m4a");
                args = FfmpegCommandBuilder.BuildAudioOnly(audioLocal, outLocal);
                command = "ffmpeg " + string.Join(' ', args);
                run = await _runner.RunAsync(args, ct);
            }

            if (!run.Success)
                return RecordingMergeResult.Fail($"ffmpeg exit {run.ExitCode}: {Tail(run.StdErr)}", command);

            if (!File.Exists(outLocal) || new FileInfo(outLocal).Length == 0)
                return RecordingMergeResult.Fail("ffmpeg produced no output file", command);

            // ── publish ────────────────────────────────────────────────────
            var outputKey = $"{request.OutputBlobPrefix.Trim('/')}/{request.CallRecordId}/merged.{ext}";
            var contentType = ext == "mp4" ? "video/mp4" : "audio/mp4";
            await using (var fs = File.OpenRead(outLocal))
                await _blobs.PutAsync(outputKey, fs, contentType, ct);

            var durationMs = await _runner.ProbeDurationMsAsync(outLocal, ct);

            _logger.LogInformation(
                "Merged recording for call {CallId} → {Key} ({Ext}, video={HadVideo}, {DurationMs}ms)",
                request.CallRecordId, outputKey, ext, hadVideo, durationMs);

            return RecordingMergeResult.Ok(
                outputKey, ext, durationMs, hadVideo,
                hadVideo ? chosen!.Id : null,
                request.ScreenRecordings.Count,
                command);
        }
        finally
        {
            TryDeleteDir(workDir);
        }
    }

    /// <summary>
    /// v1 heuristic: among screen captures with at least one chunk, take the longest
    /// (<c>DurationMs</c>), earliest-started as the tie-break. Warm-transfer calls can have
    /// several — full multi-segment stitching is a later pass.
    /// </summary>
    private static ScreenRecordingInput? SelectScreenRecording(IReadOnlyList<ScreenRecordingInput> candidates) =>
        candidates
            .Where(s => s.ChunkIndices.Count > 0)
            .OrderByDescending(s => s.DurationMs ?? 0)
            .ThenBy(s => s.StartedAtServer)
            .FirstOrDefault();

    private async Task<bool> TryAssembleScreenAsync(ScreenRecordingInput screen, string destPath, CancellationToken ct)
    {
        var ext = ChunkExtension(screen.Container);
        try
        {
            await using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            foreach (var idx in screen.ChunkIndices.OrderBy(i => i))
            {
                var key = $"{screen.StorageKey}/{idx:D6}.{ext}";
                await using var chunk = await _blobs.OpenReadAsync(key, ct);
                if (chunk is null)
                {
                    _logger.LogWarning("Screen chunk blob missing: {Key}", key);
                    return false;
                }
                await chunk.CopyToAsync(dest, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed assembling screen capture {ScreenId}", screen.Id);
            return false;
        }

        return new FileInfo(destPath).Length > 0;
    }

    private static string ChunkExtension(string container) =>
        string.Equals(container, "mp4", StringComparison.OrdinalIgnoreCase) ? "mp4" : "webm";

    private static string Tail(string s, int max = 600) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s.Trim() : s[^max..].Trim());

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best effort — temp dir */ }
    }
}
