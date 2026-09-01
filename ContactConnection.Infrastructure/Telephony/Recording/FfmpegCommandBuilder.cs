using System.Globalization;

namespace ContactConnection.Infrastructure.Telephony.Recording;

/// <summary>
/// Builds ffmpeg argument vectors for the two recording-merge shapes. Pure — no I/O, no process
/// launch — so the argument logic (stream mapping, A/V time alignment) is unit-testable on its own.
/// </summary>
public static class FfmpegCommandBuilder
{
    /// <summary>
    /// Audio-only output: transcode the stereo call WAV to AAC in an <c>.m4a</c> container.
    /// Used when the call has no usable screen capture.
    /// </summary>
    public static IReadOnlyList<string> BuildAudioOnly(string audioPath, string outputPath) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
        "-i", audioPath,
        "-c:a", "aac", "-b:a", "96k",
        "-movflags", "+faststart",
        outputPath,
    ];

    /// <summary>
    /// Mux output: the call audio is the spine (kept in full, from t0 — compliance), the screen
    /// capture is the video track, shifted onto the audio timeline by
    /// <paramref name="videoOffsetSeconds"/> = screenStartedAtServer − recordingStartedAt.
    /// Positive → screen started after audio, delay the video in (<c>-itsoffset</c>); negative →
    /// screen started first, trim its head (<c>-ss</c>).
    /// </summary>
    public static IReadOnlyList<string> BuildMux(
        string audioPath, string screenPath, double videoOffsetSeconds, string outputPath)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
            "-i", audioPath,
        };

        // Offset applies to the NEXT input (the screen capture).
        if (videoOffsetSeconds >= 0)
        {
            args.Add("-itsoffset");
            args.Add(FormatSeconds(videoOffsetSeconds));
            args.Add("-i");
            args.Add(screenPath);
        }
        else
        {
            args.Add("-ss");
            args.Add(FormatSeconds(-videoOffsetSeconds));
            args.Add("-i");
            args.Add(screenPath);
        }

        args.AddRange(new[]
        {
            "-map", "0:a", "-map", "1:v",
            "-c:a", "aac", "-b:a", "96k",
            "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p",
            // Guarantee even dimensions — yuv420p requires them.
            "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
            "-movflags", "+faststart",
            outputPath,
        });

        return args;
    }

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("0.###", CultureInfo.InvariantCulture);
}
