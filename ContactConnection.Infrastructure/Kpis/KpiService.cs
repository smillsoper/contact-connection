using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Kpis;

/// <param name="GroupBy"><c>none</c> or a <see cref="KpiDimension"/>; <paramref name="GroupBy2"/> breaks each of those down
/// again (with a subtotal row per first-level group). <paramref name="TimeZone"/>: the zone day / hour rows are in.
/// <paramref name="CampaignIds"/>: when set, only these campaigns (a client dashboard's locked scope, S181) — applied on top
/// of <paramref name="ClientId"/> / <paramref name="CampaignId"/>; an empty set matches nothing.</param>
public sealed record KpiQuery(DateTimeOffset Since, DateTimeOffset Until, Guid? ClientId, Guid? CampaignId, string GroupBy,
    string? GroupBy2 = null, string TimeZone = "UTC", IReadOnlySet<Guid>? CampaignIds = null);

/// <param name="Label2">The second-level value (two-dimension reports).</param>
/// <param name="Subtotal">A first-level group's subtotal row (two-dimension reports).</param>
public sealed record KpiRow(string Key, string Label, KpiMetrics Metrics, string? Label2 = null, bool Subtotal = false);

public sealed record KpiResult(KpiMetrics Total, IReadOnlyList<KpiRow> Rows, DateTimeOffset Since, DateTimeOffset Until);

/// <summary>
/// Loads production data for the KPI widget (S181) and runs <see cref="KpiCalculator"/> per group and for the total.
///
///   interactions — finished in the window (completed, else started), on calls run in production, by the interaction's own
///                  campaign (a transferred call counts once in each campaign); categories excluded from KPIs dropped
///   calls        — inbound production calls created in the window, by the call's campaign (call-handling KPIs)
///   agent time   — state history of the agents who worked the group's interactions (revenue per agent hour, ACW)
/// </summary>
public sealed class KpiService(ScopedTenantDbContextFactory dbFactory)
{
    public async Task<KpiResult> ComputeAsync(KpiQuery q, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();

        var campaigns = await db.Campaigns.AsNoTracking().Select(c => new { c.Id, c.Name, c.ClientId }).ToListAsync(ct);
        var campaignClient = campaigns.ToDictionary(c => c.Id, c => c.ClientId);
        var campaignName = campaigns.ToDictionary(c => c.Id, c => c.Name);
        var clientName = await db.Clients.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        bool InScope(Guid campaignId, Guid clientId) =>
            (q.CampaignId is null || q.CampaignId == campaignId) && (q.ClientId is null || q.ClientId == clientId)
            && (q.CampaignIds is null || q.CampaignIds.Contains(campaignId));

        // ── Disposition catalog: disposition → category ──
        var categories = await db.DispositionCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var dispositionCategory = await db.Dispositions.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.CategoryId, ct);
        var custom = (await db.CustomKpis.AsNoTracking().Where(k => k.IsActive).OrderBy(k => k.DisplayOrder).ThenBy(k => k.Name).ToListAsync(ct))
            .Select(k => new KpiCustomDefinition(k.Id, k.Name, k.NumeratorCategoryIds, k.DenominatorCategoryIds, k.Kind, k.Formula, k.Format)).ToList();
        var names = await VariableNamesAsync(db, ct);

        // ── Interactions ──
        var rawIx = await db.CallInteractions.AsNoTracking()
            .Join(db.CallRecords.AsNoTracking(), i => i.CallRecordId, r => r.Id, (i, r) => new
            {
                i.Id, i.AgentId, IxCampaign = i.CampaignId, RecordCampaign = r.CampaignId, r.ClientId, r.RunMode,
                i.DispositionId, i.Disposition, i.OrderSubmittedAt, i.Cart, i.StartedAt, i.CompletedAt,
                r.Dnis, r.MediaAttribution, IxFields = i.CustomFields, RecordFields = r.CustomFields,
            })
            .Where(x => x.RunMode == CallRunMode.Production
                        && (x.CompletedAt ?? x.StartedAt) >= q.Since && (x.CompletedAt ?? x.StartedAt) < q.Until)
            .ToListAsync(ct);
        var ixIds = rawIx.Select(x => x.Id).ToList();
        var payments = (await db.PaymentTransactions.AsNoTracking()
                .Where(p => p.InteractionId != null && ixIds.Contains(p.InteractionId.Value))
                .Select(p => new { p.InteractionId, p.Status, p.VoidedAt }).ToListAsync(ct))
            .GroupBy(p => p.InteractionId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        // Report dimensions (S181): names for ids, the tenant zone for day / hour.
        var zone = ResolveZone(q.TimeZone);
        var agentName = await db.Agents.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.FullName, ct);
        var dims = new[] { q.GroupBy, q.GroupBy2 }.Where(KpiDimension.IsValid).Select(d => d!).Distinct().ToList();
        string CampaignLabel(Guid id) => id == Guid.Empty ? "Not routed to a campaign" : campaignName.GetValueOrDefault(id, "Unknown campaign");
        string ClientLabel(Guid id) => id == Guid.Empty ? "No client" : clientName.GetValueOrDefault(id, "Unknown client");
        string AgentLabel(Guid? id) => id is { } a ? agentName.GetValueOrDefault(a, "Unknown agent") : "No agent";
        Dictionary<string, string>? Dims(Guid campaignId, Guid clientId, Guid? agentId, DateTimeOffset? at, string? dnis,
            MediaAttribution? media, string? fieldsJson, string? fallbackFieldsJson, string? disposition, string? category, bool isCall)
        {
            if (dims.Count == 0) return null;
            var d = new Dictionary<string, string>();
            foreach (var dim in dims)
            {
                string? value = dim switch
                {
                    KpiDimension.Campaign => CampaignLabel(campaignId),
                    KpiDimension.Client => ClientLabel(clientId),
                    KpiDimension.Agent => AgentLabel(agentId),
                    KpiDimension.Disposition => isCall ? null : disposition ?? "No disposition",
                    KpiDimension.Category => isCall ? null : category ?? "No category",
                    KpiDimension.Day => at is { } t ? TimeZoneInfo.ConvertTime(t, zone).ToString("yyyy-MM-dd ddd") : "No date",
                    KpiDimension.Hour => at is { } h ? TimeZoneInfo.ConvertTime(h, zone).ToString("HH:00") : "No time",
                    KpiDimension.Agency => media?.Agency is { Length: > 0 } ag ? ag : "No media agency",
                    KpiDimension.Station => media?.Station is { Length: > 0 } st ? st : "No station",
                    KpiDimension.Dnis => string.IsNullOrWhiteSpace(dnis) ? "No number" : dnis,
                    _ => FieldValue(fieldsJson, dim[KpiDimension.CustomFieldPrefix.Length..])
                         ?? FieldValue(fallbackFieldsJson, dim[KpiDimension.CustomFieldPrefix.Length..]) ?? "(blank)",
                };
                if (value is not null) d[dim] = value;
            }
            return d;
        }

        var interactions = new List<KpiInteraction>();
        foreach (var x in rawIx)
        {
            var campaignId = x.IxCampaign is { } c && c != Guid.Empty ? c : x.RecordCampaign;
            var clientId = campaignClient.GetValueOrDefault(campaignId, x.ClientId);
            if (!InScope(campaignId, clientId)) continue;

            var category = x.DispositionId is { } d && dispositionCategory.TryGetValue(d, out var catId) ? categories.GetValueOrDefault(catId) : null;
            if (category?.ExcludedFromKpis == true) continue;

            var pays = payments.GetValueOrDefault(x.Id) ?? [];
            var approved = pays.Any(p => p.Status == PaymentTransactionStatus.Approved && p.VoidedAt == null);
            var hasOrder = x.OrderSubmittedAt is not null;
            var cart = x.Cart;
            var items = cart?.Items ?? [];
            interactions.Add(new KpiInteraction(
                campaignId, clientId, x.AgentId,
                category?.SalesOpportunity == true, category?.Key, category?.Id,
                x.DispositionId is null && !string.IsNullOrWhiteSpace(x.Disposition),
                hasOrder,
                NetOrder: hasOrder && (pays.Count == 0 || approved),
                Declined: pays.Any(p => p.Status == PaymentTransactionStatus.Declined) && !approved,
                RevenueGross: cart?.CartTotal ?? 0,
                RevenueExclTax: cart is null ? 0 : cart.CartTotal - cart.SalesTax,
                RevenueMerch: cart is null ? 0 : CommissionCalculator.OrderBasis(cart),
                Units: items.Sum(i => i.Quantity),
                HasUpsell: items.Any(i => i.IsUpsell),
                HandleSeconds: x.CompletedAt is { } done && x.StartedAt is { } started ? (done - started).TotalSeconds : 0,
                DispositionId: x.DispositionId,
                Dims: Dims(campaignId, clientId, x.AgentId, x.CompletedAt ?? x.StartedAt, x.Dnis, x.MediaAttribution,
                    x.IxFields, x.IxCampaign is { } own && own != x.RecordCampaign ? null : x.RecordFields,
                    string.IsNullOrWhiteSpace(x.Disposition) ? null : x.Disposition.Trim(),
                    category?.Name ?? (string.IsNullOrWhiteSpace(x.Disposition) ? null : "Unmapped"), isCall: false)));
        }

        // ── Calls (call handling) ──
        var rawCalls = await db.CallRecords.AsNoTracking()
            .Where(r => r.RunMode == CallRunMode.Production && r.Source == CallSource.Inbound && r.CreatedAt >= q.Since && r.CreatedAt < q.Until)
            .Select(r => new { r.Id, r.CampaignId, r.ClientId, r.CreatedAt, r.Dnis, r.MediaAttribution, r.CustomFields }).ToListAsync(ct);
        var callIds = rawCalls.Select(r => r.Id).ToList();
        var states = (await db.CallStateHistory.AsNoTracking().Where(s => callIds.Contains(s.CallRecordId))
                .Select(s => new { s.CallRecordId, s.Sequence, s.State, s.EnteredAt, s.MetServiceLevel, s.AgentId }).ToListAsync(ct))
            .GroupBy(s => s.CallRecordId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Sequence).ToList());
        // Test calls (S181): a call whose dispositioned interactions are all in a category excluded from KPIs is left out of
        // call handling too — test calls never count toward offered / handled / AHT / service level, as in TMS View.
        var excludedCategories = categories.Values.Where(c => c.ExcludedFromKpis).Select(c => c.Id).ToHashSet();
        var testCalls = excludedCategories.Count == 0 ? new HashSet<Guid>() : (await db.CallInteractions.AsNoTracking()
                .Where(i => callIds.Contains(i.CallRecordId) && i.DispositionId != null)
                .Select(i => new { i.CallRecordId, i.DispositionId }).ToListAsync(ct))
            .GroupBy(i => i.CallRecordId)
            .Where(g => g.All(i => dispositionCategory.TryGetValue(i.DispositionId!.Value, out var c) && excludedCategories.Contains(c)))
            .Select(g => g.Key).ToHashSet();

        var calls = new List<KpiCall>();
        foreach (var r in rawCalls)
        {
            var clientId = campaignClient.GetValueOrDefault(r.CampaignId, r.ClientId);
            if (!InScope(r.CampaignId, clientId) || testCalls.Contains(r.Id)) continue;
            var history = states.GetValueOrDefault(r.Id) ?? [];
            double talk = 0;
            for (var i = 0; i < history.Count; i++)
                if (history[i].State == "active" && i + 1 < history.Count)
                    talk += (history[i + 1].EnteredAt - history[i].EnteredAt).TotalSeconds;
            calls.Add(new KpiCall(r.CampaignId, clientId,
                Handled: history.Any(s => s.State == "active"),
                Abandoned: history.Any(s => s.State == "abandoned"),
                // The call's first answer counts — a later re-bridge (take-over, transfer) isn't a second SL event.
                MetServiceLevel: history.FirstOrDefault(s => s.MetServiceLevel is not null)?.MetServiceLevel,
                TalkSeconds: Math.Max(0, talk),
                // The agent who took the call (first "active" state) — call handling broken down by agent.
                Dims: Dims(r.CampaignId, clientId, history.FirstOrDefault(s => s.State == "active")?.AgentId, r.CreatedAt, r.Dnis,
                    r.MediaAttribution, r.CustomFields, null, null, null, isCall: true)));
        }

        // ── Agent time ──
        var agentIds = interactions.Where(i => i.AgentId is not null).Select(i => i.AgentId!.Value).Distinct().ToList();
        var lookback = q.Since.AddDays(-2);   // the state an agent was already in when the window opened
        var agentHistory = await db.AgentStateHistory.AsNoTracking()
            .Where(h => agentIds.Contains(h.AgentId) && h.EnteredAt >= lookback && h.EnteredAt < q.Until)
            .Select(h => new { h.AgentId, h.StateCode, h.EnteredAt }).ToListAsync(ct);
        var agentTime = KpiCalculator.AgentTime(agentHistory.Select(h => (h.AgentId, h.StateCode, h.EnteredAt)), q.Since, q.Until);

        KpiMetrics Metrics(IEnumerable<KpiInteraction> ix, IEnumerable<KpiCall> cs)
        {
            var list = ix.ToList();
            var agents = list.Where(i => i.AgentId is not null).Select(i => i.AgentId!.Value).Distinct()
                .Select(a => agentTime.GetValueOrDefault(a)).OfType<KpiAgentTime>().ToList();
            return KpiCalculator.Compute(list, cs.ToList(), agents, custom, names);
        }

        var rows = new List<KpiRow>();
        if (KpiDimension.IsValid(q.GroupBy))
        {
            var d1 = q.GroupBy;
            var d2 = KpiDimension.IsValid(q.GroupBy2) && q.GroupBy2 != d1 ? q.GroupBy2 : null;
            static string? Of(IReadOnlyDictionary<string, string>? dims, string dim) => dims?.GetValueOrDefault(dim);
            foreach (var v1 in interactions.Select(i => Of(i.Dims, d1)).Concat(calls.Select(c => Of(c.Dims, d1)))
                         .OfType<string>().Distinct().Order(StringComparer.OrdinalIgnoreCase))
            {
                var ix1 = interactions.Where(i => Of(i.Dims, d1) == v1).ToList();
                var calls1 = calls.Where(c => Of(c.Dims, d1) == v1).ToList();
                if (d2 is null)
                {
                    rows.Add(new KpiRow(v1, v1, Metrics(ix1, calls1)));
                    continue;
                }
                foreach (var v2 in ix1.Select(i => Of(i.Dims, d2)).Concat(calls1.Select(c => Of(c.Dims, d2)))
                             .OfType<string>().Distinct().Order(StringComparer.OrdinalIgnoreCase))
                    rows.Add(new KpiRow($"{v1}|{v2}", v1,
                        Metrics(ix1.Where(i => Of(i.Dims, d2) == v2), calls1.Where(c => Of(c.Dims, d2) == v2)), v2));
                rows.Add(new KpiRow($"{v1}|", v1, Metrics(ix1, calls1), null, Subtotal: true));
            }
        }
        return new KpiResult(Metrics(interactions, calls), rows, q.Since, q.Until);
    }

    private static TimeZoneInfo ResolveZone(string? id)
    {
        try { return string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception) { return TimeZoneInfo.Utc; }
    }

    /// <summary>A custom field's value from a call's / interaction's custom-field snapshot, as text.</summary>
    private static string? FieldValue(string? json, string field)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) is JsonObject o && o.FirstOrDefault(kv => string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase)).Value is { } v
                ? (v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : v.ToJsonString()) is { Length: > 0 } text ? text : null
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The tenant's category and disposition variable names (<c>Cat_…</c>, <c>Disp_…</c>).</summary>
    public static async Task<KpiVariableNames> VariableNamesAsync(TenantDbContext db, CancellationToken ct)
    {
        var categories = await db.DispositionCategories.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var dispositions = await db.Dispositions.AsNoTracking().Select(d => new { d.Id, d.Name }).ToListAsync(ct);
        return new KpiVariableNames(
            KpiFormula.Names(categories.Select(c => (c.Id, c.Name)), "Cat_"),
            KpiFormula.Names(dispositions.Select(d => (d.Id, d.Name)), "Disp_"));
    }

    /// <summary>Every variable a formula can use, grouped for the designer's sidebar.</summary>
    public async Task<IReadOnlyList<KpiVariable>> VariablesAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var names = await VariableNamesAsync(db, ct);
        var categoryLabels = await db.DispositionCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var dispositionLabels = await db.Dispositions.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        return KpiFormula.Fixed
            .Concat(names.Categories.OrderBy(n => n.Value).Select(n => new KpiVariable(n.Value, $"Interactions in “{categoryLabels.GetValueOrDefault(n.Key)}”", "Categories")))
            .Concat(names.Dispositions.OrderBy(n => n.Value).Select(n => new KpiVariable(n.Value, $"Interactions dispositioned “{dispositionLabels.GetValueOrDefault(n.Key)}”", "Dispositions")))
            .ToList();
    }
}
