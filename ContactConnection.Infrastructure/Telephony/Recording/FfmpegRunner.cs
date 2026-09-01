using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.Recording;

/// <summary>
/// Thin seam over the <c>ffmpeg</c> / <c>ffprobe</c> CLIs. Kept separate from
/// <see cref="FfmpegRecordingMerger"/> so the merger's blob orchestration can be tested with a
/// fake runner and no media toolchain present.
/// </summary>
public interface IFfmpegRunner
{
    /// <summary>Runs ffmpeg with the given argument vector. Returns exit code + captured stderr.</summary>
    Task<FfmpegRunResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default);

    /// <summary>ffprobe the container duration of a media file, in milliseconds. Null if it can't be determined.</summary>
    Task<long?> ProbeDurationMsAsync(string path, CancellationToken ct = default);
}

public sealed record FfmpegRunResult(int ExitCode, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public sealed class FfmpegRunner : IFfmpegRunner
{
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly int _timeoutSeconds;
    private readonly ILogger<FfmpegRunner> _logger;

    public FfmpegRunner(IConfiguration config, ILogger<FfmpegRunner> logger)
    {
        // Recording:Ffmpeg:Path wins; fall back to the existing FreeSWITCH:FfmpegPath dev setting,
        // then bare "ffmpeg" (on PATH). ffprobe is assumed to sit next to ffmpeg.
        _ffmpegPath  = config["Recording:Ffmpeg:Path"]
                       ?? config["FreeSWITCH:FfmpegPath"]
                       ?? "ffmpeg";
        _ffprobePath = config["Recording:Ffmpeg:ProbePath"] ?? DeriveProbePath(_ffmpegPath);
        _timeoutSeconds = int.TryParse(config["Recording:Ffmpeg:TimeoutSeconds"], out var v) && v > 0 ? v : 300;
        _logger      = logger;
    }

    private static string DeriveProbePath(string ffmpegPath)
    {
        if (ffmpegPath is "ffmpeg" or "ffmpeg.exe") return "ffprobe";
        var dir  = Path.GetDirectoryName(ffmpegPath);
        var name = Path.GetFileName(ffmpegPath).Replace("ffmpeg", "ffprobe", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
    }

    public async Task<FfmpegRunResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = _ffmpegPath,
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        _logger.LogDebug("ffmpeg {Args}", string.Join(' ', args));

        using var proc = new Process { StartInfo = psi };
        var stderr = new System.Text.StringBuilder();
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.OutputDataReceived += (_, _) => { };

        if (!proc.Start())
            return new FfmpegRunResult(-1, "ffmpeg failed to start");

        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

        try
        {
            await proc.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(proc);
            return new FfmpegRunResult(-2, $"ffmpeg timed out after {_timeoutSeconds}s");
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        return new FfmpegRunResult(proc.ExitCode, stderr.ToString());
    }

    public async Task<long?> ProbeDurationMsAsync(string path, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = _ffprobePath,
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        foreach (var a in new[]
                 {
                     "-v", "error",
                     "-show_entries", "format=duration",
                     "-of", "default=noprint_wrappers=1:nokey=1",
                     path,
                 })
            psi.ArgumentList.Add(a);

        try
        {
            using var proc = new Process { StartInfo = psi };
            if (!proc.Start()) return null;

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0) return null;

            var text = stdout.Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                ? (long)Math.Round(seconds * 1000)
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffprobe duration read failed for {Path}", path);
            return null;
        }
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }
}
