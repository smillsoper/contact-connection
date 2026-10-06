using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Ai;

/// <summary>
/// The dispositions a call's script can record (AI step 2, S171) — the allowed values for the AI's
/// suggested disposition, so the model can only choose a real one. Read from the script itself, not a
/// hard-coded list, so it works for any campaign:
/// <list type="number">
/// <item>find the "Set Call Record Value" steps that write the <c>disposition</c> field, and the flow
/// variable they copy from (e.g. <c>{{flow.disposition}}</c>);</item>
/// <item>collect every value the script can give that variable — the options of questions that save into
/// it, and fixed values assigned by set-variable steps ("Disposition: Order" → Order).</item>
/// </list>
/// </summary>
public static partial class DispositionCatalog
{
    public const string FieldName = "disposition";

    /// <param name="interactionId">Only that interaction's scripts (S178 — CS has its own dispositions). Null = the call's.</param>
    public static async Task<List<string>> ForCallAsync(TenantDbContext db, Guid callRecordId, CancellationToken ct, Guid? interactionId = null)
    {
        var ix = interactionId is { } want && want != Guid.Empty ? want : (Guid?)null;

        // The campaign's disposition catalog comes first (S181) — the same list scripts pick from and KPIs read.
        // Campaigns without one fall back to the values their scripts can record.
        var fromCatalog = await FromManagedCatalogAsync(db, callRecordId, ix, ct);
        if (fromCatalog.Count > 0) return fromCatalog;

        var flowIds = await db.FlowSessions.AsNoTracking().Where(s => s.CallRecordId == callRecordId && (ix == null || s.InteractionId == ix))
            .Select(s => s.FlowId).Distinct().ToListAsync(ct);
        var definitions = await db.Flows.AsNoTracking().Where(f => flowIds.Contains(f.Id)).Select(f => f.Definition).ToListAsync(ct);
        return FromDefinitions(definitions);
    }

    /// <summary>The active catalog dispositions for the interaction's campaign (narrowest scope wins per name).</summary>
    private static async Task<List<string>> FromManagedCatalogAsync(TenantDbContext db, Guid callRecordId, Guid? interactionId, CancellationToken ct)
    {
        var record = await db.CallRecords.AsNoTracking().Where(r => r.Id == callRecordId)
            .Select(r => new { r.CampaignId, r.ClientId }).FirstOrDefaultAsync(ct);
        if (record is null) return [];
        var ixCampaign = interactionId is { } id
            ? await db.CallInteractions.AsNoTracking().Where(i => i.Id == id).Select(i => i.CampaignId).FirstOrDefaultAsync(ct)
            : null;
        var campaignId = ixCampaign is { } c && c != Guid.Empty ? c : record.CampaignId;
        var clientId = campaignId == record.CampaignId ? record.ClientId
            : await db.Campaigns.AsNoTracking().Where(x => x.Id == campaignId).Select(x => x.ClientId).FirstOrDefaultAsync(ct);
        var active = await db.Dispositions.AsNoTracking().Where(d => d.IsActive).ToListAsync(ct);
        return active.Where(d => d.AppliesTo(clientId, campaignId))
            .GroupBy(d => ContactConnection.Domain.Entities.Disposition.Normalize(d.Name))
            .Select(g => g.OrderBy(d => d.ScopeRank).First().Name)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static List<string> FromDefinitions(IEnumerable<string> definitions)
    {
        var values = new List<string>();
        foreach (var json in definitions)
        {
            if (JsonNode.Parse(json)?["nodes"] is not JsonObject nodes) continue;
            var all = nodes.Select(kv => kv.Value).OfType<JsonObject>().ToList();

            // 1. Which flow variables feed the disposition field?
            var variables = all
                .Where(n => Str(n, "type") == "set_custom_field" && Str(n, "definitionFieldName") == FieldName)
                .Select(n => FlowVar().Match(Str(n, "value") ?? "")).Where(m => m.Success)
                // Case-sensitive, like the flow engine's variables — "Disposition" and "disposition" are different
                // variables at runtime, so treating them as one here would report dispositions that never arrive.
                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            if (variables.Count == 0) continue;

            foreach (var n in all)
            {
                // 2a. Questions that save their answer into one of those variables → their options.
                if (Str(n, "type") == "input" && Str(n, "outputVariable") is { } output && variables.Contains(output)
                    && n["options"] is JsonArray options)
                    values.AddRange(options.OfType<JsonObject>().Select(o => Str(o, "value")).OfType<string>());

                // 2b. Set-variable steps that assign a fixed value to one of them.
                if (Str(n, "type") == "set_variable" && n["assignments"] is JsonArray assignments)
                    foreach (var a in assignments.OfType<JsonObject>())
                        if (FlowVar().Match(Str(a, "variable") ?? "") is { Success: true } m && variables.Contains(m.Groups[1].Value)
                            && Str(a, "value") is { } v && !v.Contains("{{"))
                            values.Add(v);
            }
        }
        return values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
    }

    private static string? Str(JsonObject n, string key) =>
        n[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    [GeneratedRegex(@"^\s*\{\{\s*flow\.([A-Za-z0-9_]+)\s*\}\}\s*$")]
    private static partial Regex FlowVar();
}
