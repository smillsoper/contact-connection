using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ContactConnection.Infrastructure.FlowDocs;

/// <summary>
/// Lays the flow document out as a PDF: a cover (branding, version, contents, colour key), then per script a set of
/// landscape chart pages and portrait "script detail" pages — every step numbered to match its box.
/// </summary>
public static class FlowDocumentPdf
{
    static FlowDocumentPdf()
    {
        // QuestPDF Community licence — free for organisations under $1M annual revenue.
        QuestPDF.Settings.License = LicenseType.Community;
        // Script text can hold anything (emoji, symbols) — a character the font lacks shows as a gap, it never fails
        // the whole document.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
    }

    private const string Ink = "#111827", Muted = "#6b7280", Faint = "#9ca3af", Rule = "#e5e7eb", Accent = "#4f46e5";

    private static readonly (StepKind Kind, string Name)[] Legend =
    [
        (StepKind.Section, "Section"), (StepKind.Speak, "Agent reads / records"), (StepKind.Decision, "Decision"),
        (StepKind.Commerce, "Cart & payment"), (StepKind.Integration, "Sends data / other scripts"), (StepKind.Audio, "Caller hears / keys"),
        (StepKind.Routing, "Queue & transfer"), (StepKind.Event, "Call event"), (StepKind.System, "Behind the scenes"), (StepKind.End, "End"),
    ];

    public static byte[] Render(FlowDocument doc)
    {
        var zone = TimeZoneOrUtc(doc.TimeZone);
        var generated = TimeZoneInfo.ConvertTime(doc.GeneratedAt, zone).ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture);
        var layouts = doc.Chapters.ToDictionary(c => c, c => c.Groups.Select(g => FlowChartLayout.Lay(g, c)).ToList());

        return Document.Create(container =>
        {
            // ── Cover ──
            container.Page(page =>
            {
                Setup(page, PageSizes.Letter, doc, null);
                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    if (doc.Logo is { } logo)
                    {
                        var box = col.Item().Height(56).AlignLeft();
                        try { if (doc.LogoIsSvg) box.Svg(System.Text.Encoding.UTF8.GetString(logo)).FitHeight(); else box.Image(logo).FitHeight(); }
                        catch (Exception) { /* an unreadable logo just isn't shown */ }
                    }
                    col.Item().PaddingTop(doc.Logo is null ? 40 : 10).Text(doc.TenantName).FontSize(13).FontColor(Muted);
                    col.Item().Text(doc.Title).FontSize(28).Bold().FontColor(Ink);
                    var first = doc.Chapters[0];
                    col.Item().Text(t =>
                    {
                        t.Span(first.IsTelephony ? "Call flow" : "Agent script").FontColor(Muted);
                        t.Span("  ·  ").FontColor(Faint);
                        if (doc.Draft) t.Span($"DRAFT version {first.Version} — not what agents and callers get today").Bold().FontColor("#b45309");
                        else t.Span($"Published version {first.Version} — what agents and callers get today").FontColor("#047857");
                    });
                    col.Item().Text($"Generated {generated} by {doc.GeneratedBy}").FontSize(9).FontColor(Faint);

                    col.Item().PaddingTop(18).Text("Contents").FontSize(13).Bold();
                    foreach (var ch in doc.Chapters)
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text(t =>
                            {
                                t.Span($"{ch.Number}.  {ch.FlowName}").Bold();
                                t.Span($"   {(ch.IsTelephony ? "call flow" : "script")} · {(ch.IsDraft ? "draft" : "published")} v{ch.Version} · {FlowDocExtractor.Plural(ch.StepsById.Count, "step", "steps")}").FontSize(9).FontColor(Muted);
                            });
                            r.ConstantItem(60).AlignRight().Text(t => t.BeginPageNumberOfSection($"ch{ch.Number}").FontColor(Muted));
                        });
                        if (ch.Warning is { } w) col.Item().PaddingLeft(18).Text(w).FontSize(9).FontColor("#b91c1c");
                        foreach (var g in ch.Groups)
                            col.Item().PaddingLeft(18).Row(r =>
                            {
                                r.RelativeItem().Text($"{g.Title}  ({g.Steps.First().Number}–{g.Steps.Last().Number})").FontSize(9.5f).FontColor(Ink);
                                r.ConstantItem(60).AlignRight().Text(t => t.BeginPageNumberOfSection($"ch{ch.Number}-{g.Key}").FontSize(9).FontColor(Muted));
                            });
                    }

                    col.Item().PaddingTop(18).Text("Reading the charts").FontSize(13).Bold();
                    col.Item().Text("Every step has a number — the same number in the chart and in the script detail. Lines show where each " +
                                    "step leads; a label on a line is the answer or result that takes that path. \"Back to 12\" marks a loop " +
                                    "back up the script; \"Goes to 45\" marks a jump to a step on another chart. Dashed boxes are steps nothing leads to.")
                        .FontSize(9.5f).FontColor(Muted).LineHeight(1.35f);
                    col.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                        foreach (var (kind, name) in Legend)
                        {
                            var (stroke, fill, _) = FlowChartSvg.Colors(kind);
                            table.Cell().PaddingVertical(2).Row(r =>
                            {
                                r.ConstantItem(26).Height(12).Border(1).BorderColor(stroke).Background(fill).CornerRadius(3);
                                r.RelativeItem().PaddingLeft(6).Text(name).FontSize(9);
                            });
                        }
                    });
                });
            });

            foreach (var ch in doc.Chapters)
            {
                var chapterLabel = $"{ch.Number}. {ch.FlowName}";

                // ── Charts (landscape) ──
                container.Page(page =>
                {
                    Setup(page, PageSizes.Letter.Landscape(), doc, ch);
                    page.Header().PaddingBottom(6).Row(r =>
                    {
                        r.RelativeItem().Text(t => { t.Span(chapterLabel).Bold(); t.Span("  ·  Chart").FontColor(Muted); });
                        r.AutoItem().Text(ch.IsDraft ? $"Draft v{ch.Version}" : $"Published v{ch.Version}").FontSize(9).FontColor(ch.IsDraft ? "#b45309" : Muted);
                    });
                    page.Content().Column(col =>
                    {
                        col.Item().Section($"ch{ch.Number}");
                        if (ch.Warning is { } w) col.Item().PaddingBottom(6).Text(w).FontColor("#b91c1c");
                        const double pageW = 720, chartH = 474, columnGap = 24;
                        var first = true;
                        foreach (var layout in layouts[ch])
                        {
                            var scale = Math.Min(0.85, pageW / layout.Width);
                            var parts = FlowChartSvg.Render(layout, chartH / scale);
                            // A narrow chart's parts sit side by side, a page holding as many columns as fit.
                            var columns = Math.Max(1, Math.Min(4, (int)((pageW + columnGap) / (layout.Width * scale + columnGap))));
                            for (var p = 0; p < parts.Count; p += columns)
                            {
                                if (!first) col.Item().PageBreak();
                                first = false;
                                if (p == 0) col.Item().Section($"ch{ch.Number}-{layout.Group.Key}");
                                var pages = (parts.Count + columns - 1) / columns;
                                var pageNo = p / columns + 1;
                                col.Item().PaddingBottom(4).Text(t =>
                                {
                                    t.Span(layout.Group.Title).FontSize(13).Bold();
                                    if (layout.Group.Subtitle is { } sub) t.Span($"   {sub}").FontSize(9).FontColor(Muted);
                                    if (pages > 1) t.Span($"   (page {pageNo} of {pages})").FontSize(9).FontColor(Muted);
                                });
                                var slice = parts.Skip(p).Take(columns).ToList();
                                col.Item().AlignCenter().Row(row =>
                                {
                                    row.Spacing((float)columnGap);
                                    foreach (var part in slice)
                                        row.AutoItem().AlignTop().Width((float)(part.Width * scale)).Height((float)(part.Height * scale)).Svg(part.Svg);
                                });
                            }
                        }
                    });
                });

                // ── Script detail (portrait) ──
                container.Page(page =>
                {
                    Setup(page, PageSizes.Letter, doc, ch);
                    page.Header().PaddingBottom(6).Row(r =>
                    {
                        r.RelativeItem().Text(t => { t.Span(chapterLabel).Bold(); t.Span("  ·  Script detail").FontColor(Muted); });
                        r.AutoItem().Text(ch.IsDraft ? $"Draft v{ch.Version}" : $"Published v{ch.Version}").FontSize(9).FontColor(ch.IsDraft ? "#b45309" : Muted);
                    });
                    page.Content().Column(col =>
                    {
                        foreach (var g in ch.Groups)
                        {
                            col.Item().PaddingTop(10).PaddingBottom(4).BorderBottom(1).BorderColor(Rule).Text(t =>
                            {
                                t.Span(g.Title).FontSize(14).Bold();
                                if (g.Subtitle is { } sub) t.Span($"   {sub}").FontSize(9).FontColor(Muted);
                            });
                            foreach (var s in g.Steps) col.Item().ShowEntire().Element(e => StepDetail(e, s, ch, doc));
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    private static void Setup(PageDescriptor page, PageSize size, FlowDocument doc, DocChapter? ch)
    {
        page.Size(size);
        page.Margin(36);
        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Ink));
        if (doc.Draft || ch?.IsDraft == true)
            page.Background().AlignCenter().AlignMiddle().Rotate(-30).Text("DRAFT").FontSize(120).Bold().FontColor("#f3f4f6");
        page.Footer().PaddingTop(6).Row(r =>
        {
            r.RelativeItem().Text($"{doc.TenantName} · {doc.Title}{(doc.Draft ? " · DRAFT" : "")}").FontSize(8).FontColor(Faint);
            r.AutoItem().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8).FontColor(Faint));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
                t.Span("  ·  Generated by ContactConnection");
            });
        });
    }

    private static void StepDetail(IContainer container, DocStep s, DocChapter ch, FlowDocument doc)
    {
        var (stroke, fill, _) = FlowChartSvg.Colors(s.Kind);
        container.PaddingTop(8).Row(row =>
        {
            row.ConstantItem(34).PaddingTop(1).Element(e =>
                e.Width(26).Height(16).Background(stroke).CornerRadius(8).AlignCenter().AlignMiddle()
                 .Text(s.Number.ToString(CultureInfo.InvariantCulture)).FontSize(8.5f).Bold().FontColor("#ffffff"));
            row.RelativeItem().Column(col =>
            {
                col.Spacing(3);
                col.Item().Text(t =>
                {
                    t.Span(s.Title).Bold().FontSize(11);
                    t.Span($"   {s.TypeLabel}").FontSize(8.5f).FontColor(stroke);
                    if (s.Unreachable) t.Span("   not connected — never reached").FontSize(8.5f).FontColor("#b91c1c");
                });
                if (s.Script.Count > 0)
                    col.Item().Background(fill).BorderLeft(3).BorderColor(stroke).PaddingVertical(5).PaddingHorizontal(8).Column(sc =>
                    {
                        sc.Spacing(3);
                        foreach (var p in s.Script)
                            sc.Item().Text(t =>
                            {
                                t.DefaultTextStyle(x => x.FontSize(9.5f).LineHeight(1.3f));
                                if (p.Bullet) t.Span("•  ");
                                foreach (var r in p.Runs)
                                {
                                    var span = t.Span(r.Text);
                                    if (r.Bold) span.Bold();
                                    if (r.Italic) span.Italic();
                                    if (r.Underline) span.Underline();
                                    if (r.Color is { } c) span.FontColor(c);
                                }
                            });
                    });
                foreach (var f in s.Facts)
                    col.Item().Text(t =>
                    {
                        t.Span($"{f.Label}: ").FontSize(9).FontColor(Muted);
                        t.Span(f.Value).FontSize(9);
                    });
                if (s.CallsFlowId is { } called && doc.Chapters.FirstOrDefault(c => c.FlowId == called) is { } target)
                    col.Item().Text($"See chapter {target.Number}: {target.FlowName}").FontSize(9).FontColor(Accent);
                if (s.Exits.Count > 0)
                    col.Item().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(9));
                        t.Span("Next: ").FontColor(Muted);
                        var first = true;
                        foreach (var e in s.Exits)
                        {
                            if (!first) t.Span("   ·   ").FontColor(Faint);
                            first = false;
                            var target = ch.StepsById.GetValueOrDefault(e.TargetId);
                            if (e.Label is { Length: > 0 }) t.Span($"{e.Label}: ").FontColor(Muted);
                            t.Span(target is null ? "(missing step)" : $"{target.Number} {target.Title}").FontColor(Accent);
                        }
                    });
            });
        });
    }

    private static TimeZoneInfo TimeZoneOrUtc(string tz)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tz); } catch (Exception) { return TimeZoneInfo.Utc; }
    }
}
