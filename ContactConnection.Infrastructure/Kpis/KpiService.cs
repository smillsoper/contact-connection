using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Kpis;

/// <param name="GroupBy"><c>none</c>, <c>campaign</c> or <c>client</c>.</param>
public sealed record KpiQuery(DateTimeOffset Since, DateTimeOffset Until, Guid? ClientId, Guid? CampaignId, string GroupBy);

public sealed record KpiRow(string Key, string Label, KpiMetrics Metrics);

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
            (q.CampaignId is null || q.CampaignId == campaignId) && (q.ClientId is null || q.ClientId == clientId);

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
            })
            .Where(x => x.RunMode == CallRunMode.Production
                        && (x.CompletedAt ?? x.StartedAt) >= q.Since && (x.CompletedAt ?? x.StartedAt) < q.Until)
            .ToListAsync(ct);
        var ixIds = rawIx.Select(x => x.Id).ToList();
        var payments = (await db.PaymentTransactions.AsNoTracking()
                .Where(p => p.InteractionId != null && ixIds.Contains(p.InteractionId.Value))
                .Select(p => new { p.InteractionId, p.Status, p.VoidedAt }).ToListAsync(ct))
            .GroupBy(p => p.InteractionId!.Value).ToDictionary(g => g.Key, g => g.ToList());

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
                DispositionId: x.DispositionId));
        }

        // ── Calls (call handling) ──
        var rawCalls = await db.CallRecords.AsNoTracking()
            .Where(r => r.RunMode == CallRunMode.Production && r.Source == CallSource.Inbound && r.CreatedAt >= q.Since && r.CreatedAt < q.Until)
            .Select(r => new { r.Id, r.CampaignId, r.ClientId }).ToListAsync(ct);
        var callIds = rawCalls.Select(r => r.Id).ToList();
        var states = (await db.CallStateHistory.AsNoTracking().Where(s => callIds.Contains(s.CallRecordId))
                .Select(s => new { s.CallRecordId, s.Sequence, s.State, s.EnteredAt, s.MetServiceLevel }).ToListAsync(ct))
            .GroupBy(s => s.CallRecordId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Sequence).ToList());
        var calls = new List<KpiCall>();
        foreach (var r in rawCalls)
        {
            var clientId = campaignClient.GetValueOrDefault(r.CampaignId, r.ClientId);
            if (!InScope(r.CampaignId, clientId)) continue;
            var history = states.GetValueOrDefault(r.Id) ?? [];
            double talk = 0;
            for (var i = 0; i < history.Count; i++)
                if (history[i].State == "active" && i + 1 < history.Count)
                    talk += (history[i + 1].EnteredAt - history[i].EnteredAt).TotalSeconds;
            calls.Add(new KpiCall(r.CampaignId, clientId,
                Handled: history.Any(s => s.State == "active"),
                Abandoned: history.Any(s => s.State == "abandoned"),
                MetServiceLevel: history.LastOrDefault(s => s.MetServiceLevel is not null)?.MetServiceLevel,
                TalkSeconds: Math.Max(0, talk)));
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

        var rows = q.GroupBy switch
        {
            "campaign" => interactions.Select(i => i.CampaignId).Concat(calls.Select(c => c.CampaignId)).Distinct()
                .Select(id => new KpiRow(id.ToString(), id == Guid.Empty ? "Not routed to a campaign" : campaignName.GetValueOrDefault(id, "Unknown campaign"),
                    Metrics(interactions.Where(i => i.CampaignId == id), calls.Where(c => c.CampaignId == id))))
                .OrderBy(r => r.Label).ToList(),
            "client" => interactions.Select(i => i.ClientId).Concat(calls.Select(c => c.ClientId)).Distinct()
                .Select(id => new KpiRow(id.ToString(), id == Guid.Empty ? "No client" : clientName.GetValueOrDefault(id, "Unknown client"),
                    Metrics(interactions.Where(i => i.ClientId == id), calls.Where(c => c.ClientId == id))))
                .OrderBy(r => r.Label).ToList(),
            _ => new List<KpiRow>(),
        };
        return new KpiResult(Metrics(interactions, calls), rows, q.Since, q.Until);
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
