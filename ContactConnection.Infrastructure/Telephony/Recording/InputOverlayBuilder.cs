using System.Globalization;
using System.Text;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Infrastructure.Telephony.Recording;

/// <summary>
/// Turns a screen capture's click / key cue points into an ASS subtitle script that ffmpeg (libass) draws onto the merged
/// video (S183): a ripple where each click landed, and a key strip along the bottom showing what was typed. Pure — no I/O.
/// Coordinates are already in the capture's own pixels (the portal maps them), so the script's play resolution is the
/// video size and positions need no scaling.
/// </summary>
public static class InputOverlayBuilder
{
    /// <summary>How long a burst of typing stays on screen after the last key.</summary>
    public const double KeyLingerSeconds = 1.5;
    private const double RippleSeconds = 0.8;
    private const int MaxStripChars = 40;

    /// <param name="videoOffsetSeconds">Where the capture starts on the output timeline (the mux offset; negative = its
    /// head was trimmed). A cue point at <c>AtMs</c> shows at <c>offset + AtMs/1000</c>; anything before 0 is dropped.</param>
    /// <returns>The script, or null when there's nothing to draw.</returns>
    public static string? Build(IReadOnlyList<ScreenRecordingCuePoint> cues, int width, int height, double videoOffsetSeconds)
    {
        if (width <= 0 || height <= 0) return null;
        var clicks = new List<(double T, int X, int Y)>();
        var keys = new List<(double T, string Label)>();
        foreach (var c in cues)
        {
            var t = videoOffsetSeconds + c.AtMs / 1000.0;
            if (t < 0) continue;
            if (c.Kind == ScreenRecordingCuePointKind.Click && TryParsePoint(c.Detail, out var x, out var y)
                && x >= 0 && y >= 0 && x <= width && y <= height)
                clicks.Add((t, x, y));
            else if (c.Kind == ScreenRecordingCuePointKind.Key && !string.IsNullOrEmpty(c.Detail))
                keys.Add((t, c.Detail));
        }
        if (clicks.Count == 0 && keys.Count == 0) return null;

        var r = Math.Max(14, height / 24);               // ripple radius
        var bord = Math.Max(3, height / 240);
        var font = Math.Max(14, height / 32);
        var sb = new StringBuilder();
        sb.Append("[Script Info]\nScriptType: v4.00+\nWrapStyle: 2\nScaledBorderAndShadow: yes\n");
        sb.Append(CultureInfo.InvariantCulture, $"PlayResX: {width}\nPlayResY: {height}\n\n");
        sb.Append("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, " +
                  "Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n");
        // Ripple: amber outline, no fill. Keys: white on a translucent dark box (BorderStyle 3), bottom-left.
        sb.Append("Style: Click,Arial,10,&HFF000000,&HFF000000,&H0000C8FF,&HFF000000,0,0,0,0,100,100,0,0,1,3,0,7,0,0,0,1\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"Style: Keys,Arial,{font},&H00FFFFFF,&H00FFFFFF,&H50101010,&H50101010,1,0,0,0,100,100,0,0,3,{Math.Max(4, font / 3)},0,1,{font},{font},{font},1\n\n");
        sb.Append("[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");

        foreach (var (t, x, y) in clicks)
        {
            // A translucent amber disc centred on the click that grows and fades (the ripple), over a solid dot with a dark
            // rim marking the exact point — readable on light and dark screens alike.
            var ms = (int)(RippleSeconds * 1000);
            sb.Append(CultureInfo.InvariantCulture,
                $@"Dialogue: 1,{Ts(t)},{Ts(t + RippleSeconds)},Click,,0,0,0,,{{\an5\pos({x},{y})\bord{bord}\blur1\1c&H00C8FF&\1a&HA0&\fscx50\fscy50\t(0,{ms},\fscx130\fscy130\1a&HFF&\3a&HFF&)\p1}}{Circle(r)}{{\p0}}");
            sb.Append('\n');
            sb.Append(CultureInfo.InvariantCulture,
                $@"Dialogue: 2,{Ts(t)},{Ts(t + RippleSeconds)},Click,,0,0,0,,{{\an5\pos({x},{y})\bord2\3c&H000000&\3a&H40&\1c&H00C8FF&\1a&H00&\fad(0,{ms / 2})\p1}}{Circle(Math.Max(5, r / 3))}{{\p0}}");
            sb.Append('\n');
        }

        // Typing runs: keys less than the linger apart build one growing line; each key shows the run so far until the next.
        var run = new StringBuilder();
        for (var i = 0; i < keys.Count; i++)
        {
            var (t, label) = keys[i];
            if (i > 0 && t - keys[i - 1].T > KeyLingerSeconds) run.Clear();
            Append(run, label);
            var end = i + 1 < keys.Count && keys[i + 1].T - t <= KeyLingerSeconds ? keys[i + 1].T : t + KeyLingerSeconds;
            var text = run.Length > MaxStripChars ? "…" + run.ToString(run.Length - MaxStripChars, MaxStripChars) : run.ToString();
            sb.Append(CultureInfo.InvariantCulture, $"Dialogue: 3,{Ts(t)},{Ts(end)},Keys,,0,0,0,,⌨ {Escape(text)}\n");
        }
        return sb.ToString();
    }

    /// <summary>Single characters type straight on; named keys (Enter, Ctrl+C…) show bracketed, space-separated.</summary>
    private static void Append(StringBuilder run, string label)
    {
        if (label.Length == 1) { run.Append(label); return; }
        if (run.Length > 0 && run[^1] != ' ') run.Append(' ');
        run.Append('[').Append(label).Append("] ");
    }

    private static string Circle(int r)
    {
        // Four cubic béziers approximating a circle (k = 0.5523), drawn in the box 0..2r — libass places a drawing by its
        // box from (0,0), so a circle centred on the origin landed a radius up-left of the click. With \an5 the box's
        // centre — the circle's centre — sits on \pos.
        var k = (int)Math.Round(r * 0.5523);
        int c = r, lo = r - k, hi = r + k, d = 2 * r;
        return string.Create(CultureInfo.InvariantCulture,
            $"m 0 {c} b 0 {lo} {lo} 0 {c} 0 b {hi} 0 {d} {lo} {d} {c} b {d} {hi} {hi} {d} {c} {d} b {lo} {d} 0 {hi} 0 {c}");
    }

    private static bool TryParsePoint(string? detail, out int x, out int y)
    {
        x = y = -1;
        var parts = detail?.Split(',');
        return parts is { Length: 2 }
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
    }

    /// <summary>ASS time: H:MM:SS.cc</summary>
    internal static string Ts(double seconds)
    {
        var cs = (long)Math.Round(Math.Max(0, seconds) * 100);
        return string.Create(CultureInfo.InvariantCulture, $"{cs / 360000}:{cs / 6000 % 60:00}:{cs / 100 % 60:00}.{cs % 100:00}");
    }

    /// <summary>Braces start override blocks and backslashes escapes in ASS — neutralise both in typed text.</summary>
    private static string Escape(string s) => s.Replace("\\", "⧵").Replace("{", "(").Replace("}", ")");
}
