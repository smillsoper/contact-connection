using System.Globalization;
using System.Security;
using System.Text;

namespace ContactConnection.Infrastructure.FlowDocs;

/// <summary>One page's worth of a chart, as SVG (chart units; the PDF scales it to the page width).</summary>
public record ChartPageSvg(string Svg, double Width, double Height, int Part, int Parts);

/// <summary>
/// Draws a <see cref="ChartLayout"/> as print-friendly SVG, cut into page-high bands. A line that crosses a page break
/// ends in "continues at 23, next page" on one page and starts with "from 12" on the next.
/// </summary>
public static class FlowChartSvg
{
    private const string Font = "Lato, Helvetica, Arial, sans-serif";

    public static (string Stroke, string Fill, string Ink) Colors(StepKind k) => k switch
    {
        StepKind.Section => ("#111827", "#1f2937", "#ffffff"),
        StepKind.Speak => ("#2563eb", "#eff6ff", "#1e3a8a"),
        StepKind.Decision => ("#d97706", "#fffbeb", "#78350f"),
        StepKind.Commerce => ("#059669", "#ecfdf5", "#064e3b"),
        StepKind.Integration => ("#4f46e5", "#eef2ff", "#312e81"),
        StepKind.Audio => ("#0d9488", "#f0fdfa", "#134e4a"),
        StepKind.Routing => ("#7c3aed", "#f5f3ff", "#4c1d95"),
        StepKind.Event => ("#be185d", "#fdf2f8", "#831843"),
        StepKind.End => ("#dc2626", "#fef2f2", "#7f1d1d"),
        _ => ("#9ca3af", "#f9fafb", "#374151"),
    };

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string X(string s) => SecurityElement.Escape(s) ?? "";

    /// <summary>Greedy word wrap by an average character width; the last line gets an ellipsis if it doesn't fit.</summary>
    public static List<string> Wrap(string text, double width, double size, int maxLines)
    {
        var perLine = Math.Max(4, (int)(width / (size * 0.52)));
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word.Length > perLine ? word[..(perLine - 1)] + "…" : word;
            if (line.Length > 0 && line.Length + 1 + w.Length > perLine)
            {
                lines.Add(line.ToString());
                line.Clear();
                if (lines.Count == maxLines) break;
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(w);
        }
        if (line.Length > 0 && lines.Count < maxLines) lines.Add(line.ToString());
        else if (lines.Count == maxLines && line.Length > 0)
            lines[^1] = (lines[^1].Length > perLine - 1 ? lines[^1][..(perLine - 1)] : lines[^1]) + "…";
        return lines;
    }

    /// <summary>Bands that fit <paramref name="bandHeight"/> chart units each.</summary>
    public static List<ChartPageSvg> Render(ChartLayout layout, double bandHeight)
    {
        List<(int First, int Last)> Split(double limit)
        {
            var list = new List<(int First, int Last)>();
            var first = 0;
            for (var r = 0; r < layout.RowTops.Count; r++)
            {
                var bottom = layout.RowTops[r] + layout.RowHeights[r];
                if (r > first && bottom - layout.RowTops[first] + 40 > limit) { list.Add((first, r - 1)); first = r; }
            }
            list.Add((first, Math.Max(first, layout.RowTops.Count - 1)));
            return list;
        }
        // As few pages as fit, then evened out — no nearly empty last page.
        var bands = Split(bandHeight);
        if (bands.Count > 1)
            for (var limit = layout.Height / bands.Count; limit < bandHeight; limit += 16)
                if (Split(limit) is { } even && even.Count == bands.Count) { bands = even; break; }

        int BandOf(int layer) => bands.FindIndex(b => layer >= b.First && layer <= b.Last);
        var pages = new List<ChartPageSvg>();
        for (var bi = 0; bi < bands.Count; bi++)
        {
            var (f, l) = bands[bi];
            var top = layout.RowTops[f] - 22;   // room for "from …" markers
            var bottom = layout.RowTops[l] + layout.RowHeights[l] + 24;
            var h = bottom - top;
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(layout.Width)} {F(h)}\" preserveAspectRatio=\"xMidYMin meet\">");
            sb.Append("<defs><marker id=\"a\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"7\" markerHeight=\"7\" orient=\"auto\"><path d=\"M0,0 L10,5 L0,10 z\" fill=\"#6b7280\"/></marker></defs>");
            double Y(double y) => y - top;

            // Page-break markers are staggered so neighbours don't print over each other.
            var markers = new List<(double X, double Y)>();
            double Slot(double x, double y, int dir)
            {
                while (markers.Any(m => Math.Abs(m.X - x) < 46 && Math.Abs(m.Y - y) < 8)) y += dir * 9;
                markers.Add((x, y));
                return y;
            }
            // Answer labels sit partway down their line; several from one box alternate heights.
            var labelIndex = new Dictionary<DocStep, int>();

            // Lines arriving from earlier pages into the same step share one line and one marker ("from 37, 39, 48").
            foreach (var arrivals in layout.Edges.Where(e => BandOf(e.ToLayer) == bi && BandOf(e.FromLayer) < bi).GroupBy(e => e.To).Where(g => g.Count() > 1))
            {
                var target = arrivals.First().Points[^1];
                var x = target.X;
                sb.Append(Path([(x, 22), (x, Y(target.Y))], true));
                var label = "from " + string.Join(", ", arrivals.Select(e => e.From.Number).Distinct().Order());
                var lines = Wrap(label, 210, 7.5, 2);
                for (var i = 0; i < lines.Count; i++)
                    sb.Append(Text(x + 3, Slot(x, 9 + i * 9, 1), lines[i], 7.5, "#6b7280", anchor: "start"));
            }
            var shared = layout.Edges.Where(e => BandOf(e.ToLayer) == bi && BandOf(e.FromLayer) < bi).GroupBy(e => e.To)
                .Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();

            // Lines first, boxes on top.
            foreach (var e in layout.Edges)
            {
                var fb = BandOf(e.FromLayer); var tb = BandOf(e.ToLayer);
                if (fb != bi && tb != bi) continue;
                if (shared.Contains(e) && tb == bi) continue;   // drawn above, as one shared line
                var pts = e.Points.Where(p => BandOf(p.Layer) == bi).Select(p => (p.X, Y(p.Y))).ToList();
                var continues = tb != bi;   // leaves this page downward
                var arrives = fb != bi;     // comes from an earlier page
                if (continues) pts.Add((pts[^1].X, h - 12));
                if (arrives) pts.Insert(0, (pts[0].X, 12));
                sb.Append(Path(pts, !continues));
                if (continues)
                    sb.Append(Text(pts[^1].X + 3, Slot(pts[^1].X, h - 3, -1), $"to {e.To.Number}", 7.5, "#6b7280", anchor: "start"));
                // An answer whose line leaves the page straight away is labelled on the next page instead ("from 10 · Order").
                var leavesAtOnce = continues && e.Points.Count(p => BandOf(p.Layer) == bi) == 1;
                if (arrives)
                {
                    var leftAtOnce = e.Points.Count(p => BandOf(p.Layer) == fb) == 1;
                    var from = leftAtOnce && e.Label is { Length: > 0 } l0 ? $"from {e.From.Number} · {l0}" : $"from {e.From.Number}";
                    sb.Append(Text(pts[0].X + 3, Slot(pts[0].X, 9, 1), Wrap(from, 150, 7.5, 1)[0], 7.5, "#6b7280", anchor: "start"));
                }
                if (e.Label is { Length: > 0 } label && !arrives && !leavesAtOnce)
                {
                    var n = labelIndex[e.From] = labelIndex.GetValueOrDefault(e.From, -1) + 1;
                    var next = e.Points.Count > 1 ? e.Points[1] : e.Points[0];
                    var t = 0.38 + (n % 2) * 0.24;
                    var (lx, ly) = (e.Points[0].X + (next.X - e.Points[0].X) * t,
                        Y(e.Points[0].Y) + Math.Min(FlowChartLayout.RowGap, next.Y - e.Points[0].Y) * t + 3);
                    var w = Math.Min(label.Length * 4.3 + 8, 120);
                    sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{F(lx - w / 2)}\" y=\"{F(ly - 8)}\" width=\"{F(w)}\" height=\"11\" rx=\"3\" fill=\"#ffffff\" stroke=\"#d1d5db\" stroke-width=\"0.6\"/>");
                    sb.Append(Text(lx, ly, Wrap(label, w - 6, 7.2, 1)[0], 7.2, "#374151"));
                }
            }

            foreach (var b in layout.Boxes.Where(b => BandOf(b.Layer) == bi))
                sb.Append(Box(b, Y(b.Y), layout.Tags.Where(t => t.From == b.Step).ToList()));

            sb.Append("</svg>");
            pages.Add(new ChartPageSvg(sb.ToString(), layout.Width, h, bi + 1, bands.Count));
        }
        return pages;
    }

    private static string Path(List<(double X, double Y)> pts, bool arrow)
    {
        if (pts.Count < 2) return "";
        var d = new StringBuilder($"M{F(pts[0].X)},{F(pts[0].Y)}");
        for (var i = 1; i < pts.Count; i++)
        {
            var (x0, y0) = pts[i - 1]; var (x1, y1) = pts[i];
            var my = (y0 + y1) / 2;
            d.Append(CultureInfo.InvariantCulture, $" C{F(x0)},{F(my)} {F(x1)},{F(my)} {F(x1)},{F(y1)}");
        }
        return $"<path d=\"{d}\" fill=\"none\" stroke=\"#9ca3af\" stroke-width=\"1.1\"{(arrow ? " marker-end=\"url(#a)\"" : "")}/>";
    }

    private static string Text(double x, double y, string s, double size, string color, bool bold = false, string anchor = "middle") =>
        $"<text x=\"{F(x)}\" y=\"{F(y)}\" font-family=\"{Font}\" font-size=\"{F(size)}\" fill=\"{color}\" text-anchor=\"{anchor}\"{(bold ? " font-weight=\"bold\"" : "")}>{X(s)}</text>";

    private static string Box(ChartBox b, double y, List<ChartTag> tags)
    {
        var s = b.Step;
        var (stroke, fill, ink) = Colors(s.Kind);
        var sb = new StringBuilder();
        var dash = s.Unreachable ? " stroke-dasharray=\"4 3\"" : "";
        sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{F(b.X)}\" y=\"{F(y)}\" width=\"{F(b.W)}\" height=\"{F(b.H)}\" rx=\"6\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"1.2\"{dash}/>");
        // Number badge + kind.
        var label = s.Kind == StepKind.Section ? "SECTION" : s.TypeLabel.ToUpperInvariant();
        sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{F(b.X + 6)}\" y=\"{F(y + 5)}\" width=\"{F(10 + s.Number.ToString(CultureInfo.InvariantCulture).Length * 5)}\" height=\"11\" rx=\"5.5\" fill=\"{stroke}\"/>");
        sb.Append(Text(b.X + 11 + s.Number.ToString(CultureInfo.InvariantCulture).Length * 2.5, y + 13.3, s.Number.ToString(CultureInfo.InvariantCulture), 7.5, "#ffffff", bold: true));
        sb.Append(Text(b.X + 22 + s.Number.ToString(CultureInfo.InvariantCulture).Length * 5, y + 13.3, Wrap(label, b.W - 40, 6.8, 1)[0], 6.8,
            s.Kind == StepKind.Section ? "#d1d5db" : stroke, anchor: "start"));

        var lines = Wrap(s.Title, b.W - 14, 10.5, s.Kind == StepKind.System ? 1 : 2);
        var ty = y + (s.Kind == StepKind.System ? 29 : s.Kind is StepKind.Section or StepKind.Event or StepKind.End ? 31 : lines.Count == 1 ? 36 : 31);
        foreach (var line in lines)
        {
            sb.Append(Text(b.X + b.W / 2, ty, line, 10.5, ink, bold: s.Kind != StepKind.System));
            ty += 12.5;
        }
        // Tags: loops back and jumps to other charts.
        var tagY = y + b.H + 11;
        foreach (var t in tags)
        {
            var txt = (t.Label is { Length: > 0 } ? $"{t.Label}: " : "") + t.Text;
            sb.Append(Text(b.X + 4, tagY, Wrap(txt, b.W + 30, 7.2, 1)[0], 7.2, t.Loop ? "#b45309" : "#4f46e5", anchor: "start"));
            tagY += FlowChartLayout.TagRoom;
        }
        return sb.ToString();
    }
}
