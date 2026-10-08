namespace ContactConnection.Infrastructure.FlowDocs;

/// <summary>A positioned box on a chart.</summary>
public record ChartBox(DocStep Step, double X, double Y, double W, double H, int Layer);

/// <summary>A drawn connection: points from the source box's bottom to the target box's top (through any rows it
/// crosses), each with the row it belongs to.</summary>
public record ChartEdge(DocStep From, DocStep To, string? Label, IReadOnlyList<(double X, double Y, int Layer)> Points, int FromLayer, int ToLayer);

/// <summary>A short tag under a box for a connection that isn't drawn as a line: a loop back up the script, or a jump
/// to a step on another chart.</summary>
public record ChartTag(DocStep From, string Text, string? Label, bool Loop);

public class ChartLayout
{
    public required DocGroup Group { get; init; }
    public List<ChartBox> Boxes { get; } = [];
    public List<ChartEdge> Edges { get; } = [];
    public List<ChartTag> Tags { get; } = [];
    public double Width { get; set; }
    public double Height { get; set; }
    /// <summary>Top y of each row.</summary>
    public List<double> RowTops { get; } = [];
    public List<double> RowHeights { get; } = [];
}

/// <summary>
/// Layered ("top to bottom") layout of one chart: loops are cut (and shown as "Back to 12" tags), steps go into rows by
/// longest path, rows are ordered to cut crossings (barycenter sweeps), and lines that skip rows bend through
/// placeholder points so they don't cut through boxes. Small and dependency-free; good for the tree-like shape of
/// contact-center scripts.
/// </summary>
public static class FlowChartLayout
{
    public const double BoxW = 184, SystemW = 156, Gap = 30, RowGap = 44, Margin = 14, TagRoom = 18;
    /// <summary>Widest a row may get before it wraps onto another line (keeps text readable once scaled to the page).</summary>
    public const double MaxRowWidth = 860;
    /// <summary>Widest the whole chart may get — wider and it's squeezed (order kept, boxes kept apart).</summary>
    public const double MaxChartWidth = 980;

    public static double WidthOf(DocStep s) => s.Kind == StepKind.System ? SystemW : BoxW;
    public static double HeightOf(DocStep s) => s.Kind switch
    {
        StepKind.System => 38,
        StepKind.Section or StepKind.Event or StepKind.End => 46,
        _ => 60,
    };

    private sealed class V
    {
        public DocStep? Step;          // null = placeholder point on a long line
        public int Layer;
        /// <summary>The drawn row — a layer too wide for the page wraps onto several rows.</summary>
        public int Row;
        public double Order;
        public double X;
        public double W;
        public List<V> Up { get; } = [];
        public List<V> Down { get; } = [];
    }

    public static ChartLayout Lay(DocGroup group, DocChapter chapter)
    {
        var layout = new ChartLayout { Group = group };
        var inGroup = group.Steps.ToDictionary(s => s.Id);
        var index = group.Steps.Select((s, i) => (s.Id, i)).ToDictionary(t => t.Id, t => t.i);

        // ── Edges inside the chart; others become tags ──
        var edges = new List<(DocStep From, DocStep To, string? Label)>();
        foreach (var s in group.Steps)
        {
            // Several answers leading to the same step draw one line ("Error / Success"); if every answer goes the same
            // way, the line needs no label at all.
            var oneWay = s.Exits.Select(e => e.TargetId).Distinct().Count() == 1;
            foreach (var same in s.Exits.Where(e => inGroup.ContainsKey(e.TargetId)).GroupBy(e => e.TargetId))
            {
                var labels = same.Select(e => e.Label).OfType<string>().Distinct().ToList();
                edges.Add((s, inGroup[same.Key], oneWay || labels.Count == 0 ? null : string.Join(" / ", labels)));
            }
            // Jumps to steps on other charts: one tag per destination.
            foreach (var same in s.Exits.Where(e => !inGroup.ContainsKey(e.TargetId)).GroupBy(e => e.TargetId))
            {
                if (!chapter.StepsById.TryGetValue(same.Key, out var other)) continue;
                var where = chapter.Groups.FirstOrDefault(g => g.Key == other.GroupKey)?.Title;
                var labels = same.Select(e => e.Label).OfType<string>().Distinct().ToList();
                layout.Tags.Add(new ChartTag(s, $"Goes to {other.Number} {other.Title}{(where is null ? "" : $" ({where})")}",
                    oneWay || labels.Count == 0 ? null : string.Join(" / ", labels), false));
            }
        }

        // ── Cut loops: an edge back to a step still on the walk's path ──
        var adj = edges.GroupBy(e => e.From.Id).ToDictionary(g => g.Key, g => g.ToList());
        var state = new Dictionary<string, int>();   // 1 = on path, 2 = done
        var back = new HashSet<(string, string, string?)>();
        void Dfs(DocStep s)
        {
            state[s.Id] = 1;
            foreach (var e in adj.GetValueOrDefault(s.Id) ?? [])
            {
                var st = state.GetValueOrDefault(e.To.Id);
                if (st == 1) back.Add((e.From.Id, e.To.Id, e.Label));
                else if (st == 0) Dfs(e.To);
            }
            state[s.Id] = 2;
        }
        foreach (var s in group.Steps) if (!state.ContainsKey(s.Id)) Dfs(s);
        foreach (var (f, t, l) in back)
            layout.Tags.Add(new ChartTag(inGroup[f], $"Back to {inGroup[t].Number} {inGroup[t].Title}", l, true));
        var forward = edges.Where(e => !back.Contains((e.From.Id, e.To.Id, e.Label))).ToList();

        // ── Rows: longest path from the top ──
        var verts = group.Steps.ToDictionary(s => s.Id, s => new V { Step = s, W = WidthOf(s) });
        var indeg = group.Steps.ToDictionary(s => s.Id, _ => 0);
        foreach (var e in forward) indeg[e.To.Id]++;
        var queue = new Queue<string>(group.Steps.Where(s => indeg[s.Id] == 0).Select(s => s.Id));
        var outs = forward.GroupBy(e => e.From.Id).ToDictionary(g => g.Key, g => g.Select(e => e.To.Id).ToList());
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var t in outs.GetValueOrDefault(id) ?? [])
            {
                verts[t].Layer = Math.Max(verts[t].Layer, verts[id].Layer + 1);
                if (--indeg[t] == 0) queue.Enqueue(t);
            }
        }

        // ── Placeholder points for lines that skip rows ──
        var all = verts.Values.ToList();
        var chains = new List<(DocStep From, DocStep To, string? Label, List<V> Path)>();
        foreach (var e in forward)
        {
            var path = new List<V> { verts[e.From.Id] };
            for (var l = verts[e.From.Id].Layer + 1; l < verts[e.To.Id].Layer; l++)
            {
                var d = new V { Layer = l, W = 8, Order = index[e.From.Id] };
                all.Add(d);
                path.Add(d);
            }
            path.Add(verts[e.To.Id]);
            for (var i = 0; i + 1 < path.Count; i++) { path[i].Down.Add(path[i + 1]); path[i + 1].Up.Add(path[i]); }
            chains.Add((e.From, e.To, e.Label, path));
        }

        var layers = all.GroupBy(v => v.Layer).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        foreach (var v in verts.Values) v.Order = index[v.Step!.Id];
        foreach (var row in layers)
        {
            var sorted = row.OrderBy(v => v.Order).ToList();
            for (var i = 0; i < sorted.Count; i++) sorted[i].Order = i;
        }

        // ── Order each row by its neighbours' average position (fewer crossings) ──
        for (var pass = 0; pass < 8; pass++)
        {
            var down = pass % 2 == 0;
            var seq = down ? layers.Skip(1) : Enumerable.Reverse(layers).Skip(1);
            foreach (var row in seq)
            {
                foreach (var v in row)
                {
                    var n = down ? v.Up : v.Down;
                    if (n.Count > 0) v.Order = n.Average(x => x.Order) + v.Order * 0.001;
                }
                var sorted = row.OrderBy(v => v.Order).ToList();
                for (var i = 0; i < sorted.Count; i++) sorted[i].Order = i;
            }
        }

        // ── X positions: pull each box toward its neighbours, keep boxes apart ──
        foreach (var row in layers)
        {
            double x = 0;
            foreach (var v in row.OrderBy(v => v.Order)) { v.X = x + v.W / 2; x += v.W + Gap; }
        }
        for (var pass = 0; pass < 12; pass++)
        {
            var down = pass % 2 == 0;
            var seq = down ? layers.Skip(1).ToList() : Enumerable.Reverse(layers).Skip(1).ToList();
            foreach (var row in seq)
            {
                var ordered = row.OrderBy(v => v.Order).ToList();
                var want = ordered.Select(v =>
                {
                    var n = (down ? v.Up : v.Down);
                    if (n.Count == 0) n = down ? v.Down : v.Up;
                    return n.Count > 0 ? n.Average(x => x.X) : v.X;
                }).ToArray();
                var pos = new double[ordered.Count];
                for (var i = 0; i < ordered.Count; i++)
                    pos[i] = i == 0 ? want[i] : Math.Max(want[i], pos[i - 1] + (ordered[i - 1].W + ordered[i].W) / 2 + Gap);
                var shift = Enumerable.Range(0, ordered.Count).Average(i => want[i] - pos[i]);
                for (var i = 0; i < ordered.Count; i++) ordered[i].X = pos[i] + shift;
            }
        }

        // ── Wrap a layer wider than the page onto extra rows (e.g. a question with ten answers) ──
        var rows = new List<List<V>>();
        foreach (var layer in layers)
        {
            var ordered = layer.OrderBy(v => v.Order).ToList();
            var total = ordered.Sum(v => v.W) + Gap * (ordered.Count - 1);
            if (total <= MaxRowWidth) { rows.Add(ordered); continue; }
            var centre = ordered.Average(v => v.X);
            var chunk = new List<V>();
            double width = 0;
            void Place()
            {
                var w = chunk.Sum(v => v.W) + Gap * (chunk.Count - 1);
                var x = centre - w / 2;
                foreach (var v in chunk) { v.X = x + v.W / 2; x += v.W + Gap; }
                rows.Add(chunk);
            }
            foreach (var v in ordered)
            {
                if (chunk.Count > 0 && width + Gap + v.W > MaxRowWidth) { Place(); chunk = []; width = 0; }
                width += (chunk.Count > 0 ? Gap : 0) + v.W;
                chunk.Add(v);
            }
            Place();
        }
        for (var r = 0; r < rows.Count; r++) foreach (var v in rows[r]) v.Row = r;

        // ── Squeeze a chart that spread too wide: scale every x toward the middle, then push boxes apart again ──
        {
            var lo = all.Min(v => v.X - v.W / 2);
            var hi = all.Max(v => v.X + v.W / 2);
            if (hi - lo > MaxChartWidth)
            {
                var mid = (lo + hi) / 2;
                var k = MaxChartWidth / (hi - lo);
                foreach (var row in rows)
                {
                    var ordered = row.OrderBy(v => v.X).ToList();
                    var want = ordered.Select(v => mid + (v.X - mid) * k).ToArray();
                    var pos = new double[ordered.Count];
                    for (var i = 0; i < ordered.Count; i++)
                        pos[i] = i == 0 ? want[i] : Math.Max(want[i], pos[i - 1] + (ordered[i - 1].W + ordered[i].W) / 2 + Gap);
                    var shift = Enumerable.Range(0, ordered.Count).Average(i => want[i] - pos[i]);
                    for (var i = 0; i < ordered.Count; i++) ordered[i].X = pos[i] + shift;
                }
            }
        }

        var minX = all.Min(v => v.X - v.W / 2);
        var maxX = all.Max(v => v.X + v.W / 2);
        foreach (var v in all) v.X += Margin - minX;
        layout.Width = maxX - minX + 2 * Margin;

        // ── Rows' y ──
        var tagsBy = layout.Tags.GroupBy(t => t.From.Id).ToDictionary(g => g.Key, g => g.Count());
        double y = Margin;
        foreach (var row in rows)
        {
            var h = row.Where(v => v.Step is not null).Select(v => HeightOf(v.Step!) + (tagsBy.GetValueOrDefault(v.Step!.Id) * TagRoom)).DefaultIfEmpty(10).Max();
            layout.RowTops.Add(y);
            layout.RowHeights.Add(h);
            y += h + RowGap;
        }
        layout.Height = y - RowGap + Margin;

        foreach (var v in verts.Values)
            layout.Boxes.Add(new ChartBox(v.Step!, v.X - v.W / 2, layout.RowTops[v.Row], v.W, HeightOf(v.Step!), v.Row));

        // ── Line points: out of the source's bottom (spread by exit), through placeholders, into the target's top ──
        // Each source's exits leave from left to right in the order of where they go, so they don't cross.
        foreach (var bySource in chains.GroupBy(c => c.From.Id))
        {
            var list = bySource.OrderBy(c => c.Path[1].X).ToList();
            for (var k = 0; k < list.Count; k++)
            {
                var c = list[k];
                var src = c.Path[0];
                var sx = src.X - src.W / 2 + src.W * (k + 1) / (list.Count + 1);
                var pts = new List<(double, double, int)> { (sx, layout.RowTops[src.Row] + HeightOf(c.From), src.Row) };
                foreach (var d in c.Path.Skip(1).SkipLast(1))
                    pts.Add((d.X, layout.RowTops[d.Row] + layout.RowHeights[d.Row] / 2, d.Row));
                var dst = c.Path[^1];
                pts.Add((dst.X, layout.RowTops[dst.Row], dst.Row));
                layout.Edges.Add(new ChartEdge(c.From, c.To, c.Label, pts, src.Row, dst.Row));
            }
        }
        return layout;
    }
}
