using System.Text;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Exports;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Exports;

/// <summary>
/// The export engine (S180). Reads the window's calls a page at a time (production calls, or training + sandbox for a
/// practice test file), builds the template model (<see cref="ExportCallModel"/>), and renders either:
///
///   Columns mode — one row per call / interaction / cart line; each column's Liquid → delimited / fixed / xlsx, streamed;
///   Document mode — one Liquid template over every call (<c>{% for call in calls %}</c>), for rigid vendor formats.
///
/// Every template also sees <c>export</c> (name, is_test, run_id, data_source, time_zone, generated_at) and <c>window</c>
/// (start, end, end_inclusive). A template error stops the file — a vendor file with a broken column is worse than none.
/// </summary>
public sealed class ExportGenerator(ScopedTenantDbContextFactory dbFactory, ISensitiveDataProtector protector) : IExportGenerator
{
    private const int PageSize = 500;

    public string? Validate(ExportSpec spec)
    {
        if (!ExportRowGrain.IsValid(spec.RowGrain)) return $"Unknown row grain '{spec.RowGrain}'.";
        if (!ExportLayoutMode.IsValid(spec.LayoutMode)) return $"Unknown layout mode '{spec.LayoutMode}'.";
        if (ResolveZone(spec.TimeZone) is null) return $"Unknown time zone '{spec.TimeZone}'.";
        if (spec.LineEnding is not ("crlf" or "lf")) return "Line ending must be crlf or lf.";
        if (string.IsNullOrWhiteSpace(spec.FileNameTemplate)) return "A file name is required.";
        // Card data (S182): only an export marked as carrying it may use the card variables — and never in a file name.
        if (ExportCardData.UsesCard(spec.FileNameTemplate)) return "The file name can't use card data.";
        if (!spec.IncludesCardData && new[] { spec.DocumentTemplate, spec.Condition }.Concat(spec.Columns.Select(c => c.Template)).Any(ExportCardData.UsesCard))
            return "This layout uses card data (card.…) — turn on \"Includes card data\" for this export, or remove it.";
        if (ExportTemplateEngine.Validate(spec.FileNameTemplate) is { } fe) return $"File name: {fe}";
        if (!string.IsNullOrWhiteSpace(spec.Condition)
            && ExportTemplateEngine.Validate(ExportTemplateEngine.ConditionTemplate(spec.Condition)) is { } ce)
            return $"Condition: {ce}";

        if (spec.LayoutMode == ExportLayoutMode.Document)
        {
            if (string.IsNullOrWhiteSpace(spec.DocumentTemplate)) return "The document template is empty.";
            return ExportTemplateEngine.Validate(spec.DocumentTemplate) is { } de ? $"Document template: {de}" : null;
        }

        if (!ExportFormat.IsValid(spec.Format)) return $"Unknown format '{spec.Format}'.";
        if (spec.Columns.Count == 0) return "Add at least one column.";
        if (spec.Format == ExportFormat.Delimited && string.IsNullOrEmpty(spec.Delimiter)) return "A delimiter is required.";
        for (var i = 0; i < spec.Columns.Count; i++)
        {
            var c = spec.Columns[i];
            var label = string.IsNullOrWhiteSpace(c.Header) ? $"Column {i + 1}" : $"Column '{c.Header}'";
            if (spec.Format == ExportFormat.Fixed && c.Width is not > 0) return $"{label} needs a width (fixed-width file).";
            if (ExportTemplateEngine.Validate(c.Template ?? "") is { } e) return $"{label}: {e}";
        }
        return null;
    }

    public string ContentType(ExportSpec spec) => spec.LayoutMode == ExportLayoutMode.Document ? "text/plain"
        : spec.Format switch
        {
            ExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ExportFormat.Delimited when spec.Delimiter == "," => "text/csv",
            _ => "text/plain",
        };

    public string RenderFileName(ExportGenerationRequest request)
    {
        var engine = new ExportTemplateEngine(ResolveZone(request.Spec.TimeZone) ?? TimeZoneInfo.Utc);
        string name;
        try { name = engine.RenderAsync(request.Spec.FileNameTemplate, Context(request)).AsTask().GetAwaiter().GetResult(); }
        catch (Exception) { name = ""; }
        name = new string(name.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
        if (name.Length == 0) name = "export.txt";
        return request.IsTest ? ExportRun.ApplyTestSuffix(name, request.Spec.TestFileSuffix) : name;
    }

    public async Task<ExportGenerationResult> GenerateAsync(ExportGenerationRequest request, Stream output, CancellationToken ct = default)
    {
        var spec = request.Spec;
        if (Validate(spec) is { } invalid) return new(false, 0, 0, false, invalid);
        var engine = new ExportTemplateEngine(ResolveZone(spec.TimeZone)!);
        var context = Context(request);

        await using var db = dbFactory.Create();
        var lookups = new ExportCallModel.Lookups(
            await db.Clients.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct),
            await db.Campaigns.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct),
            await db.Agents.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.FullName, ct),
            (await db.Offers.AsNoTracking().Select(o => new { o.Id, o.Flags }).ToListAsync(ct))
                .ToDictionary(o => o.Id, o => (IReadOnlyList<ContactConnection.Domain.ValueObjects.Commerce.ProductFlag>)o.Flags),
            await DispositionLookupAsync(db, ct));

        var calls = 0;
        var rows = 0;
        var truncated = false;
        string? where = null;
        var cardCalls = new List<Guid>();
        try
        {
            if (spec.LayoutMode == ExportLayoutMode.Document)
            {
                var models = new List<object?>();
                var numbers = new List<string>();
                await foreach (var (call, id, card) in Calls(db, request, lookups, ct))
                {
                    if (request.MaxCalls is { } max && calls >= max) { truncated = true; break; }
                    where = Where(call);
                    if (!await Passes(engine, spec, Model(context, call))) continue;
                    models.Add(call);
                    calls++;
                    if (card?.Number is { } n) { numbers.Add(n); if (card.Real) cardCalls.Add(id); }
                }
                where = "the document template";
                var model = new Dictionary<string, object?>(context) { ["calls"] = models, ["call_count"] = models.Count };
                var text = ExportTemplateEngine.NormalizeLines(
                    await engine.RenderAsync(spec.DocumentTemplate!, model, document: true), spec.LineEnding, spec.SkipBlankLines);
                if (!spec.IncludesCardData && numbers.Any(text.Contains)) return LeakResult(rows, calls);
                rows = text.Length == 0 ? 0 : text.Count(ch => ch == '\n');
                var bytes = ExportRowWriter.Utf8NoBom.GetBytes(text);
                await output.WriteAsync(bytes, ct);
                return new(true, rows, calls, truncated, null, cardCalls);
            }

            await using var writer = ExportRowWriter.For(spec, output);
            if (spec.IncludeHeader) await writer.WriteHeaderAsync(spec.Columns);
            var values = new string[spec.Columns.Count];
            await foreach (var (call, id, card) in Calls(db, request, lookups, ct))
            {
                if (request.MaxCalls is { } max && calls >= max) { truncated = true; break; }
                var counted = false;
                foreach (var row in RowModels(context, call, spec))
                {
                    where = Where(call);
                    if (!await Passes(engine, spec, row)) continue;
                    row["row_number"] = rows + 1;
                    for (var i = 0; i < spec.Columns.Count; i++)
                    {
                        where = $"{Where(call)}, column '{spec.Columns[i].Header}'";
                        values[i] = await engine.RenderAsync(spec.Columns[i].Template ?? "", row);
                    }
                    // A call's real card number must never reach a file that isn't marked as carrying card data (S182).
                    if (!spec.IncludesCardData && card?.Number is { } pan && values.Any(v => v.Contains(pan))) return LeakResult(rows, calls);
                    await writer.WriteRowAsync(spec.Columns, values);
                    rows++;
                    counted = true;
                }
                if (counted)
                {
                    calls++;
                    if (card is { Real: true }) cardCalls.Add(id);
                }
            }
            await writer.CompleteAsync();
            return new(true, rows, calls, truncated, null, cardCalls);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, rows, calls, truncated, where is null ? ex.Message : $"{where}: {ex.Message}");
        }
    }

    // ── Data ───────────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyDictionary<Guid, (Disposition, DispositionCategory?)>> DispositionLookupAsync(TenantDbContext db, CancellationToken ct)
    {
        var categories = await db.DispositionCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        return (await db.Dispositions.AsNoTracking().ToListAsync(ct))
            .ToDictionary(d => d.Id, d => (d, categories.GetValueOrDefault(d.CategoryId)));
    }

    /// <summary>A call's captured card (S182): its number (for the leak guard) and whether real values were exported.</summary>
    private sealed record CardInfo(string? Number, bool Real);

    private static ExportGenerationResult LeakResult(int rows, int calls) => new(false, rows, calls, false,
        "This file would contain a caller's card number, but the export isn't marked as including card data. Remove the field that carries it, or (if card data is meant to go out) turn on \"Includes card data\".");

    private async IAsyncEnumerable<(Dictionary<string, object?> Call, Guid Id, CardInfo? Card)> Calls(
        TenantDbContext db, ExportGenerationRequest request, ExportCallModel.Lookups lookups,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var spec = request.Spec;
        var start = request.WindowStart.ToUniversalTime();
        var end = request.WindowEnd.ToUniversalTime();
        var q = db.CallRecords.AsNoTracking().Include(r => r.Interactions)
            .Where(r => r.CallStartAt != null && r.CallStartAt >= start && r.CallStartAt < end);
        q = request.DataSource == ExportDataSource.Practice
            ? q.Where(r => r.RunMode != CallRunMode.Production)
            : q.Where(r => r.RunMode == CallRunMode.Production);
        if (spec.ClientId is { } clientId) q = q.Where(r => r.ClientId == clientId);
        if (spec.CampaignIds.Count > 0)
        {
            var ids = spec.CampaignIds;
            q = q.Where(r => ids.Contains(r.CampaignId)
                             || r.Interactions.Any(i => i.CampaignId != null && ids.Contains(i.CampaignId.Value)));
        }
        q = q.OrderBy(r => r.CallStartAt).ThenBy(r => r.Id);

        for (var page = 0; ; page++)
        {
            var batch = await q.Skip(page * PageSize).Take(PageSize).AsSplitQuery().ToListAsync(ct);
            if (batch.Count == 0) yield break;

            var ids = batch.Select(r => r.Id).ToList();
            var payments = (await db.PaymentTransactions.AsNoTracking()
                    .Where(p => ids.Contains(p.CallRecordId) && p.Status == PaymentTransactionStatus.Approved && p.VoidedAt == null)
                    .ToListAsync(ct))
                .GroupBy(p => p.CallRecordId).ToDictionary(g => g.Key, g => (IReadOnlyList<PaymentTransaction>)g.ToList());

            foreach (var r in batch)
            {
                if (!string.IsNullOrWhiteSpace(spec.MediaAgency)
                    && !string.Equals(r.MediaAttribution?.Agency, spec.MediaAgency.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                var json = ExportCallModel.Build(r, lookups, payments.GetValueOrDefault(r.Id) ?? []);
                var call = (Dictionary<string, object?>)FluidLiquidTemplateRenderer.ToPlain(json)!;
                var fields = CardFields(r.SensitiveData);
                CardInfo? card = fields is null ? null : new(ExportCardData.Number(fields), Real: spec.IncludesCardData && !request.MaskCardData);
                // The card variables exist only on a card-data export; masked on previews and test files.
                if (spec.IncludesCardData) call["card"] = fields is null ? null : ExportCardData.Build(fields, request.MaskCardData);
                yield return (call, r.Id, card);
            }
            if (batch.Count < PageSize) yield break;
        }
    }

    private Dictionary<string, string>? CardFields(string? sensitive)
    {
        if (string.IsNullOrEmpty(sensitive) || !protector.IsConfigured) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Unprotect(sensitive)); }
        catch (Exception) { return null; }
    }

    /// <summary>The row models for one call, by grain. Interaction / cart-line rows honour the campaign filter per
    /// interaction (a transferred call's other campaign doesn't leak in).</summary>
    private static IEnumerable<Dictionary<string, object?>> RowModels(
        IReadOnlyDictionary<string, object?> context, Dictionary<string, object?> call, ExportSpec spec)
    {
        if (spec.RowGrain == ExportRowGrain.Call)
        {
            yield return Model(context, call);
            yield break;
        }

        var campaignFilter = spec.CampaignIds.Select(g => g.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ix in (call["interactions"] as List<object?> ?? []).OfType<Dictionary<string, object?>>())
        {
            if (campaignFilter.Count > 0
                && !(ix["campaign"] is Dictionary<string, object?> c && campaignFilter.Contains(c["id"] as string ?? "")))
                continue;

            if (spec.RowGrain == ExportRowGrain.Interaction)
            {
                var m = Model(context, call);
                m["interaction"] = ix;
                yield return m;
                continue;
            }

            var items = (ix["cart"] as Dictionary<string, object?>)?["items"] as List<object?> ?? [];
            var index = 0;
            foreach (var line in items)
            {
                var m = Model(context, call);
                m["interaction"] = ix;
                m["line"] = line;
                m["line_number"] = ++index;
                yield return m;
            }
        }
    }

    /// <summary>A row's model. A card-data export's card is also top-level (<c>card.number</c>) for row layouts; document
    /// layouts loop over calls and read <c>call.card.number</c>.</summary>
    private static Dictionary<string, object?> Model(IReadOnlyDictionary<string, object?> context, Dictionary<string, object?> call)
    {
        var m = new Dictionary<string, object?>(context) { ["call"] = call };
        if (call.TryGetValue("card", out var card)) m["card"] = card;
        return m;
    }

    private static async Task<bool> Passes(ExportTemplateEngine engine, ExportSpec spec, Dictionary<string, object?> model) =>
        string.IsNullOrWhiteSpace(spec.Condition) || await engine.TestAsync(spec.Condition, model);

    private static string Where(Dictionary<string, object?> call) => $"Call {call["id"]} ({call["started_at"]})";

    /// <summary>The <c>export</c> + <c>window</c> objects every template sees (also the email subject's model).</summary>
    public static Dictionary<string, object?> Context(ExportGenerationRequest r) => new()
    {
        ["export"] = new Dictionary<string, object?>
        {
            ["name"] = r.ExportName,
            ["is_test"] = r.IsTest,
            // scheduled / manual / test / rerun — a vendor format with an original-vs-resend flag (Cannella SF's O/R) reads is_rerun.
            ["kind"] = r.Kind,
            ["is_rerun"] = r.Kind == ExportRunKind.Rerun,
            ["run_id"] = r.RunId?.ToString() ?? "",
            ["data_source"] = r.DataSource,
            ["time_zone"] = r.Spec.TimeZone,
            ["generated_at"] = ExportCallModel.Time(DateTimeOffset.UtcNow),
        },
        ["window"] = new Dictionary<string, object?>
        {
            ["start"] = ExportCallModel.Time(r.WindowStart),
            ["end"] = ExportCallModel.Time(r.WindowEnd),
            // The last second inside the window — "the day this file covers" for a previous-day window.
            ["end_inclusive"] = ExportCallModel.Time(r.WindowEnd.AddSeconds(-1)),
        },
    };

    public static TimeZoneInfo? ResolveZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }
}
