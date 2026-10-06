using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.Recording;

/// <summary>
/// "Keep conversation only" (S181): cuts a finished recording so it starts where the caller reached an agent. Both copies
/// are cut — the raw stereo WAV on disk (stream copy: exact for PCM) and the merged playback file in blob storage
/// (re-encoded from the cut so it's exact; video re-encoded with it to stay in sync).
/// </summary>
public sealed class RecordingTrimmer(IFfmpegRunner runner, IBlobStorage blobs, ILogger<RecordingTrimmer> logger)
{
    public async Task<string?> TrimAsync(string? rawPath, string? mergedBlobKey, double offsetSeconds, CancellationToken ct = default)
    {
        if (offsetSeconds <= 0.05) return null;   // nothing before the conversation to remove
        var workDir = Path.Combine(Path.GetTempPath(), "cc-recording-trim", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var ss = offsetSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            if (rawPath is not null && File.Exists(rawPath))
            {
                var outRaw = Path.Combine(workDir, "raw.wav");
                var run = await runner.RunAsync(["-y", "-ss", ss, "-i", rawPath, "-c", "copy", outRaw], ct);
                if (!run.Success || !File.Exists(outRaw)) return $"trimming the raw audio failed (ffmpeg exit {run.ExitCode})";
                File.Copy(outRaw, rawPath, overwrite: true);
            }

            if (mergedBlobKey is not null)
            {
                var ext = Path.GetExtension(mergedBlobKey).TrimStart('.').ToLowerInvariant();
                var inFile = Path.Combine(workDir, "in." + ext);
                await using (var blob = await blobs.OpenReadAsync(mergedBlobKey, ct))
                {
                    if (blob is null) return "the merged recording is missing";
                    await using var file = File.Create(inFile);
                    await blob.CopyToAsync(file, ct);
                }
                var outFile = Path.Combine(workDir, "out." + ext);
                IReadOnlyList<string> args = ext == "mp4"
                    ? ["-y", "-ss", ss, "-i", inFile, "-c:v", "libx264", "-preset", "veryfast", "-c:a", "aac", outFile]
                    : ["-y", "-ss", ss, "-i", inFile, "-c:a", "aac", outFile];
                var run = await runner.RunAsync(args, ct);
                if (!run.Success || !File.Exists(outFile) || new FileInfo(outFile).Length == 0)
                    return $"trimming the merged recording failed (ffmpeg exit {run.ExitCode})";
                await using var outStream = File.OpenRead(outFile);
                await blobs.PutAsync(mergedBlobKey, outStream, ext == "mp4" ? "video/mp4" : "audio/mp4", ct);
            }
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Recording trim failed");
            return ex.Message;
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* temp cleanup is best-effort */ }
        }
    }
}
