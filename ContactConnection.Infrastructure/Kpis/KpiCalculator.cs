namespace ContactConnection.Infrastructure.Kpis;

/// <summary>One interaction as the KPIs see it (S181, docs/dispositions-kpi-plan.md). Production only; the loader has
/// already dropped interactions whose category is excluded from KPIs (test calls).</summary>
public sealed record KpiInteraction(
    Guid CampaignId, Guid ClientId, Guid? AgentId,
    /// <summary>Category is a sales opportunity (gross / net denominator).</summary>
    bool SalesOpportunity,
    /// <summary>The built-in category key ("sale"…) or null.</summary>
    string? CategoryKey,
    Guid? CategoryId,
    /// <summary>Recorded text that matches nothing in the catalog.</summary>
    bool Unmapped,
    bool HasOrder,
    /// <summary>An order whose payment went through (approved, not voided) — or one that took no card payment.</summary>
    bool NetOrder,
    /// <summary>A card was declined and no order went through.</summary>
    bool Declined,
    decimal RevenueGross, decimal RevenueExclTax, decimal RevenueMerch,
    int Units, bool HasUpsell,
    double HandleSeconds);

/// <summary>One inbound production call as the call-handling KPIs see it.</summary>
public sealed record KpiCall(Guid CampaignId, Guid ClientId, bool Handled, bool Abandoned, bool? MetServiceLevel, double TalkSeconds);

/// <summary>An agent's time in the window: logged in (any state but logged out) and after-call work.</summary>
public sealed record KpiAgentTime(double LoggedInSeconds, double AcwSeconds, int AcwSegments);

/// <summary>A tenant-defined KPI: share of interactions in the numerator categories out of those in the denominator
/// categories (empty denominator = all interactions).</summary>
public sealed record KpiCustomDefinition(Guid Id, string Name, IReadOnlyList<Guid> Numerator, IReadOnlyList<Guid> Denominator);

public sealed record KpiRevenue(
    decimal Total, decimal Net, decimal? PerCall, decimal? PerOpportunity, decimal? AverageOrder,
    decimal? PerAgentHour, decimal? PerTalkHour);

public sealed record KpiCustomValue(Guid Id, string Name, int Numerator, int Denominator, double? Percent);

public sealed record KpiMetrics(
    int Interactions, int Opportunities, int Orders, int NetOrders, int Declines,
    double? RawCloseRate, double? GrossCloseRate, double? NetCloseRate,
    KpiRevenue Gross, KpiRevenue ExclTax, KpiRevenue Merch,
    double? UpsellTakeRate, double? UnitsPerOrder,
    int CallsOffered, int CallsHandled, int CallsAbandoned, double? AbandonRate, double? ServiceLevel,
    double? AvgTalkSeconds, double? AvgAcwSeconds, double? AhtSeconds,
    double LoggedInHours, double TalkHours,
    int SaleWithoutOrder, int Unmapped,
    IReadOnlyList<KpiCustomValue> Custom);

/// <summary>
/// The KPI formulas (S181, docs/dispositions-kpi-plan.md) — pure, so every number is unit-tested.
///
///   raw close rate   = orders ÷ interactions
///   gross close rate = orders on sales-opportunity interactions ÷ sales-opportunity interactions
///   net close rate   = net orders on sales-opportunity interactions ÷ sales-opportunity interactions
///   revenue          = gross (cart total) / excl. tax (total − sales tax) / merchandise (total − shipping − tax − fees,
///                      the commission basis); "net" = the same over net orders only
///   per call / per opportunity / average order / per agent hour (logged-in) / per talk hour
///   AHT              = average talk + average after-call work
/// </summary>
public static class KpiCalculator
{
    public static KpiMetrics Compute(
        IReadOnlyCollection<KpiInteraction> ix, IReadOnlyCollection<KpiCall> calls,
        IReadOnlyCollection<KpiAgentTime> agents, IReadOnlyCollection<KpiCustomDefinition>? custom = null)
    {
        var orders = ix.Where(i => i.HasOrder).ToList();
        var opportunities = ix.Count(i => i.SalesOpportunity);
        var ordersOnOpp = orders.Count(i => i.SalesOpportunity);
        var netOrdersOnOpp = orders.Count(i => i.SalesOpportunity && i.NetOrder);
        var loggedInHours = agents.Sum(a => a.LoggedInSeconds) / 3600.0;
        var talkSeconds = calls.Sum(c => c.TalkSeconds);
        var talkHours = talkSeconds / 3600.0;

        KpiRevenue Revenue(Func<KpiInteraction, decimal> amount)
        {
            var total = orders.Sum(amount);
            var net = orders.Where(o => o.NetOrder).Sum(amount);
            return new(Round(total), Round(net),
                Per(total, ix.Count), Per(total, opportunities), Per(total, orders.Count),
                loggedInHours > 0 ? Round(total / (decimal)loggedInHours) : null,
                talkHours > 0 ? Round(total / (decimal)talkHours) : null);
        }

        var handled = calls.Where(c => c.Handled).ToList();
        var slCalls = calls.Where(c => c.MetServiceLevel is not null).ToList();
        var acwSegments = agents.Sum(a => a.AcwSegments);
        double? avgTalk = handled.Count > 0 ? handled.Average(c => c.TalkSeconds) : null;
        double? avgAcw = acwSegments > 0 ? agents.Sum(a => a.AcwSeconds) / acwSegments : null;

        var customValues = (custom ?? []).Select(k =>
        {
            var denominator = k.Denominator.Count == 0 ? ix.Count : ix.Count(i => i.CategoryId is { } c && k.Denominator.Contains(c));
            var numerator = ix.Count(i => i.CategoryId is { } c && k.Numerator.Contains(c)
                && (k.Denominator.Count == 0 || k.Denominator.Contains(c)));
            return new KpiCustomValue(k.Id, k.Name, numerator, denominator, Rate(numerator, denominator));
        }).ToList();

        return new KpiMetrics(
            ix.Count, opportunities, orders.Count, orders.Count(o => o.NetOrder), ix.Count(i => i.Declined && !i.HasOrder),
            Rate(orders.Count, ix.Count), Rate(ordersOnOpp, opportunities), Rate(netOrdersOnOpp, opportunities),
            Revenue(i => i.RevenueGross), Revenue(i => i.RevenueExclTax), Revenue(i => i.RevenueMerch),
            Rate(orders.Count(o => o.HasUpsell), orders.Count),
            orders.Count > 0 ? Math.Round(orders.Sum(o => o.Units) / (double)orders.Count, 2) : null,
            calls.Count, handled.Count, calls.Count(c => c.Abandoned), Rate(calls.Count(c => c.Abandoned), calls.Count),
            Rate(slCalls.Count(c => c.MetServiceLevel == true), slCalls.Count),
            Seconds(avgTalk), Seconds(avgAcw), avgTalk is null && avgAcw is null ? null : Seconds((avgTalk ?? 0) + (avgAcw ?? 0)),
            Math.Round(loggedInHours, 2), Math.Round(talkHours, 2),
            ix.Count(i => i.CategoryKey == "sale" && !i.HasOrder), ix.Count(i => i.Unmapped),
            customValues);
    }

    /// <summary>A percentage to one decimal place, or null when there's nothing to divide by.</summary>
    public static double? Rate(int part, int whole) => whole > 0 ? Math.Round(part * 100.0 / whole, 1) : null;

    private static decimal? Per(decimal total, int count) => count > 0 ? Round(total / count) : null;
    private static decimal Round(decimal d) => Math.Round(d, 2, MidpointRounding.AwayFromZero);
    private static double? Seconds(double? s) => s is null ? null : Math.Round(s.Value, 1);

    /// <summary>Time in each agent state inside [since, until), from the state-change history: each entry lasts until the
    /// agent's next entry (or until <paramref name="until"/>), clipped to the window.</summary>
    public static Dictionary<Guid, KpiAgentTime> AgentTime(
        IEnumerable<(Guid AgentId, string State, DateTimeOffset EnteredAt)> history, DateTimeOffset since, DateTimeOffset until)
    {
        var result = new Dictionary<Guid, KpiAgentTime>();
        foreach (var agent in history.GroupBy(h => h.AgentId))
        {
            var entries = agent.OrderBy(e => e.EnteredAt).ToList();
            double loggedIn = 0, acw = 0;
            var acwSegments = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var start = entries[i].EnteredAt < since ? since : entries[i].EnteredAt;
                var end = i + 1 < entries.Count ? entries[i + 1].EnteredAt : until;
                if (end > until) end = until;
                if (end <= start) continue;
                var seconds = (end - start).TotalSeconds;
                if (entries[i].State != "logged_out") loggedIn += seconds;
                if (entries[i].State == "acw") { acw += seconds; acwSegments++; }
            }
            result[agent.Key] = new KpiAgentTime(loggedIn, acw, acwSegments);
        }
        return result;
    }
}
