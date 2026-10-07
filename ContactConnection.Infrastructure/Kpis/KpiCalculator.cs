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
    double HandleSeconds,
    /// <summary>The catalog disposition (formula KPIs count dispositions as well as categories).</summary>
    Guid? DispositionId = null,
    /// <summary>Report dimensions → this interaction's label (agent, day, station…), see <see cref="KpiDimension"/>.</summary>
    IReadOnlyDictionary<string, string>? Dims = null);

/// <summary>One inbound production call as the call-handling KPIs see it.</summary>
public sealed record KpiCall(Guid CampaignId, Guid ClientId, bool Handled, bool Abandoned, bool? MetServiceLevel, double TalkSeconds,
    IReadOnlyDictionary<string, string>? Dims = null);

/// <summary>What a KPI report can be broken down by (S181). Calls have no disposition / category, so call-handling KPIs
/// show "—" on those rows. <c>cf:&lt;field name&gt;</c> breaks down by a custom field's value.</summary>
public static class KpiDimension
{
    public const string Campaign = "campaign";
    public const string Client = "client";
    public const string Agent = "agent";
    public const string Disposition = "disposition";
    public const string Category = "category";
    public const string Day = "day";
    public const string Hour = "hour";
    public const string Agency = "agency";
    public const string Station = "station";
    public const string Dnis = "dnis";
    // Time axes for charts (S182): intervals as "yyyy-MM-dd HH:mm" (sortable), week = its Monday, month, day of week.
    public const string Interval15 = "interval15";
    public const string Interval30 = "interval30";
    public const string Interval60 = "interval60";
    public const string Week = "week";
    public const string Month = "month";
    public const string DayOfWeek = "dow";
    public const string CustomFieldPrefix = "cf:";

    public static bool IsValid(string? d) =>
        d is Campaign or Client or Agent or Disposition or Category or Day or Hour or Agency or Station or Dnis
            or Interval15 or Interval30 or Interval60 or Week or Month or DayOfWeek
        || (d?.StartsWith(CustomFieldPrefix, StringComparison.Ordinal) == true && d.Length > CustomFieldPrefix.Length);

    /// <summary>Dimensions whose values are periods of time — their empty periods are filled with zeros.</summary>
    public static bool IsTime(string? d) => d is Day or Hour or Interval15 or Interval30 or Interval60 or Week or Month or DayOfWeek;

    public static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    /// <summary>The label a moment gets on a time dimension, in the report's zone (local time already applied).</summary>
    public static string? TimeLabel(string dim, DateTime local) => dim switch
    {
        Day => local.ToString("yyyy-MM-dd ddd", System.Globalization.CultureInfo.InvariantCulture),
        Hour => local.ToString("HH:00", System.Globalization.CultureInfo.InvariantCulture),
        Interval15 or Interval30 or Interval60 => Floor(local, dim == Interval15 ? 15 : dim == Interval30 ? 30 : 60)
            .ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        Week => local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        Month => local.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
        DayOfWeek => DayNames[((int)local.DayOfWeek + 6) % 7],
        _ => null,
    };

    private static DateTime Floor(DateTime t, int minutes) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute / minutes * minutes, 0, t.Kind);

    /// <summary>Every label of a time dimension between two local moments (inclusive start, exclusive end), in order.</summary>
    public static IEnumerable<string> TimeLabels(string dim, DateTime fromLocal, DateTime toLocal)
    {
        if (dim == Hour) { foreach (var h in Enumerable.Range(0, 24)) yield return $"{h:00}:00"; yield break; }
        if (dim == DayOfWeek) { foreach (var n in DayNames) yield return n; yield break; }
        var step = dim switch
        {
            Interval15 => TimeSpan.FromMinutes(15), Interval30 => TimeSpan.FromMinutes(30), Interval60 => TimeSpan.FromHours(1),
            Day => TimeSpan.FromDays(1), Week => TimeSpan.FromDays(7), _ => TimeSpan.Zero,
        };
        var seen = new HashSet<string>();
        if (dim == Month)
        {
            for (var m = new DateTime(fromLocal.Year, fromLocal.Month, 1); m < toLocal; m = m.AddMonths(1))
                yield return m.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            yield break;
        }
        var start = dim == Week ? fromLocal.Date.AddDays(-(((int)fromLocal.DayOfWeek + 6) % 7)) : dim == Day ? fromLocal.Date
            : Floor(fromLocal, dim == Interval15 ? 15 : dim == Interval30 ? 30 : 60);
        for (var t = start; t < toLocal && seen.Count < 5000; t += step)
            if (TimeLabel(dim, t) is { } label && seen.Add(label)) yield return label;
    }

    /// <summary>Sort order for a dimension's values — days of the week in week order, everything else as text.</summary>
    public static IComparer<string> Order(string dim) => dim == DayOfWeek
        ? Comparer<string>.Create((a, b) => Array.IndexOf(DayNames, a).CompareTo(Array.IndexOf(DayNames, b)))
        : StringComparer.OrdinalIgnoreCase;
}

/// <summary>An agent's time in the window: logged in (any state but logged out) and after-call work.</summary>
public sealed record KpiAgentTime(double LoggedInSeconds, double AcwSeconds, int AcwSegments);

/// <summary>A tenant-defined KPI: either a ratio (share of interactions in the numerator categories out of those in the
/// denominator categories; empty denominator = all interactions) or an NCalc formula over the KPI variables
/// (<see cref="KpiFormula"/>), shown in <paramref name="Format"/>.</summary>
public sealed record KpiCustomDefinition(Guid Id, string Name, IReadOnlyList<Guid> Numerator, IReadOnlyList<Guid> Denominator,
    string Kind = KpiCustomKind.Ratio, string? Formula = null, string Format = KpiFormat.Percent);

public static class KpiCustomKind
{
    public const string Ratio = "ratio";
    public const string Formula = "formula";
}

/// <summary>How a formula KPI's value is shown. Percent multiplies by 100 (a formula returns a fraction).</summary>
public static class KpiFormat
{
    public const string Number = "number";
    public const string Currency = "currency";
    public const string Percent = "percent";
    public const string Duration = "duration";
    public const string Integer = "integer";
    public static bool IsValid(string? v) => v is Number or Currency or Percent or Duration or Integer;
}

/// <summary>Raw totals behind the metrics — the variables formula KPIs read.</summary>
public sealed record KpiRaw(
    int Units, int UpsellOrders, double TalkSeconds, double AcwSeconds, double HandleSeconds, double LoggedInSeconds,
    IReadOnlyDictionary<Guid, int> CategoryCounts, IReadOnlyDictionary<Guid, int> DispositionCounts);

/// <summary>Variable names for the tenant's categories and dispositions (<see cref="KpiFormula.Names"/>).</summary>
public sealed record KpiVariableNames(IReadOnlyDictionary<Guid, string> Categories, IReadOnlyDictionary<Guid, string> Dispositions);

public sealed record KpiRevenue(
    decimal Total, decimal Net, decimal? PerCall, decimal? PerOpportunity, decimal? AverageOrder,
    decimal? PerAgentHour, decimal? PerTalkHour);

/// <param name="Percent">Ratio KPIs: the percentage. <paramref name="Value"/> is the display value for every kind (a
/// formula KPI in percent format is already ×100).</param>
public sealed record KpiCustomValue(Guid Id, string Name, int Numerator, int Denominator, double? Percent,
    string Kind = KpiCustomKind.Ratio, string Format = KpiFormat.Percent, double? Value = null);

public sealed record KpiMetrics(
    int Interactions, int Opportunities, int Orders, int NetOrders, int Declines,
    double? RawCloseRate, double? GrossCloseRate, double? NetCloseRate,
    KpiRevenue Gross, KpiRevenue ExclTax, KpiRevenue Merch,
    double? UpsellTakeRate, double? UnitsPerOrder,
    int CallsOffered, int CallsHandled, int CallsAbandoned, double? AbandonRate, double? ServiceLevel,
    double? AvgTalkSeconds, double? AvgAcwSeconds, double? AhtSeconds,
    double LoggedInHours, double TalkHours,
    int SaleWithoutOrder, int Unmapped,
    IReadOnlyList<KpiCustomValue> Custom,
    KpiRaw Raw);

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
        IReadOnlyCollection<KpiAgentTime> agents, IReadOnlyCollection<KpiCustomDefinition>? custom = null,
        KpiVariableNames? names = null)
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

        var raw = new KpiRaw(
            orders.Sum(o => o.Units), orders.Count(o => o.HasUpsell), talkSeconds, agents.Sum(a => a.AcwSeconds),
            ix.Sum(i => i.HandleSeconds), agents.Sum(a => a.LoggedInSeconds),
            ix.Where(i => i.CategoryId is not null).GroupBy(i => i.CategoryId!.Value).ToDictionary(g => g.Key, g => g.Count()),
            ix.Where(i => i.DispositionId is not null).GroupBy(i => i.DispositionId!.Value).ToDictionary(g => g.Key, g => g.Count()));

        var metrics = new KpiMetrics(
            ix.Count, opportunities, orders.Count, orders.Count(o => o.NetOrder), ix.Count(i => i.Declined && !i.HasOrder),
            Rate(orders.Count, ix.Count), Rate(ordersOnOpp, opportunities), Rate(netOrdersOnOpp, opportunities),
            Revenue(i => i.RevenueGross), Revenue(i => i.RevenueExclTax), Revenue(i => i.RevenueMerch),
            Rate(orders.Count(o => o.HasUpsell), orders.Count),
            orders.Count > 0 ? Math.Round(orders.Sum(o => o.Units) / (double)orders.Count, 2) : null,
            calls.Count, handled.Count, calls.Count(c => c.Abandoned), Rate(calls.Count(c => c.Abandoned), calls.Count),
            Rate(slCalls.Count(c => c.MetServiceLevel == true), slCalls.Count),
            // AHT needs handled calls — after-call work alone (agents who only worked transfers here) isn't a handle time.
            Seconds(avgTalk), Seconds(avgAcw), avgTalk is null ? null : Seconds(avgTalk.Value + (avgAcw ?? 0)),
            Math.Round(loggedInHours, 2), Math.Round(talkHours, 2),
            ix.Count(i => i.CategoryKey == "sale" && !i.HasOrder), ix.Count(i => i.Unmapped),
            [], raw);

        if (custom is not { Count: > 0 }) return metrics;
        var variables = names is null ? null : KpiFormula.Values(metrics, names.Categories, names.Dispositions);
        return metrics with { Custom = custom.Select(k => k.Kind == KpiCustomKind.Formula ? FormulaValue(k, variables) : RatioValue(k, ix)).ToList() };
    }

    private static KpiCustomValue RatioValue(KpiCustomDefinition k, IReadOnlyCollection<KpiInteraction> ix)
    {
        var denominator = k.Denominator.Count == 0 ? ix.Count : ix.Count(i => i.CategoryId is { } c && k.Denominator.Contains(c));
        var numerator = ix.Count(i => i.CategoryId is { } c && k.Numerator.Contains(c)
            && (k.Denominator.Count == 0 || k.Denominator.Contains(c)));
        var pct = Rate(numerator, denominator);
        return new KpiCustomValue(k.Id, k.Name, numerator, denominator, pct, KpiCustomKind.Ratio, KpiFormat.Percent, pct);
    }

    private static KpiCustomValue FormulaValue(KpiCustomDefinition k, IReadOnlyDictionary<string, double>? variables)
    {
        var result = variables is null || string.IsNullOrWhiteSpace(k.Formula) ? null : KpiFormula.Evaluate(k.Formula, variables);
        double? value = result is not { } v ? null : k.Format switch
        {
            KpiFormat.Percent => Math.Round(v * 100, 1),
            KpiFormat.Integer => Math.Round(v),
            KpiFormat.Duration => Math.Round(v, 1),
            _ => Math.Round(v, 2),
        };
        return new KpiCustomValue(k.Id, k.Name, 0, 0, null, KpiCustomKind.Formula, k.Format, value);
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
