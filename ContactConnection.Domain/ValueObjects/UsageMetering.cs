namespace ContactConnection.Domain.ValueObjects;

/// <summary>
/// Usage metering for per-minute billing (S174). Tenants pay all-in per billed carrier minute, with a toll-free
/// surcharge, so the meter counts only calls that crossed the PSTN:
/// <list type="bullet">
/// <item><b>Inbound:</b> a real caller (PSTN ANI) to a real number (PSTN DNIS). Toll-free or local is decided by
/// the number dialed.</item>
/// <item><b>Outbound / callback:</b> a real number was dialed (PSTN in <c>CallerId</c>).</item>
/// </list>
/// Internal calls (extensions, designer test sessions) never reach the carrier and are not billed.
/// </summary>
public static class BillableNumber
{
    private static readonly HashSet<string> TollFreeAreaCodes = ["800", "833", "844", "855", "866", "877", "888"];

    /// <summary>The 10-digit NANP form of a number, or null when it isn't one (extensions, blanks, foreign).</summary>
    public static string? Nanp(string? number)
    {
        var digits = new string((number ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
        return digits.Length == 10 ? digits : null;
    }

    public static bool IsPstn(string? number) => Nanp(number) is not null;

    public static bool IsTollFree(string? number) => Nanp(number) is { } n && TollFreeAreaCodes.Contains(n[..3]);
}

public enum UsageCategory { InboundLocal, InboundTollFree, Outbound }

/// <summary>One call as the meter sees it. Billed from <c>StartedAt</c> to <c>DisconnectedAt</c> (the hang-up); never to when
/// the record was closed or finalized, which includes wrap-up. <c>Closed</c>: the record has been closed.</summary>
public sealed record MeteredCall(string Source, string? CallerId, string? Dnis, DateTimeOffset? StartedAt,
    DateTimeOffset? DisconnectedAt, bool Closed);

public sealed class UsageLine
{
    public int Calls { get; internal set; }
    /// <summary>Actual connected time, for reference and reconciliation.</summary>
    public long Seconds { get; internal set; }
    public decimal Minutes => Math.Round(Seconds / 60m, 2);

    /// <summary>What gets billed: each call rounded up to the whole minute, the way the carrier bills us (SignalWire CDR,
    /// verified S175: 10 s → 1 min, 132 s → 3 min).</summary>
    public long BilledMinutes { get; internal set; }

    internal void AddCall(long seconds)
    {
        Calls++;
        Seconds += seconds;
        BilledMinutes += (seconds + 59) / 60;
    }
}

public sealed class UsageTally
{
    public UsageLine InboundLocal { get; } = new();
    public UsageLine InboundTollFree { get; } = new();
    public UsageLine Outbound { get; } = new();

    /// <summary>Inbound minutes per number dialed, for checking against the carrier's per-number usage.</summary>
    public Dictionary<string, UsageLine> ByNumber { get; } = [];

    /// <summary>Calls that crossed the PSTN and are still open (live now, or not yet swept).</summary>
    public int Unended { get; private set; }

    /// <summary>Billable calls closed without a recorded hang-up (the startup orphan sweep): the real length is unknown,
    /// so they are held for review, not billed.</summary>
    public int NeedsReview { get; private set; }

    /// <summary>Internal calls skipped (extension-to-extension, designer tests).</summary>
    public int Internal { get; private set; }

    public static UsageCategory? Categorize(MeteredCall c) => c.Source switch
    {
        "inbound" when BillableNumber.IsPstn(c.CallerId) && BillableNumber.IsPstn(c.Dnis) =>
            BillableNumber.IsTollFree(c.Dnis) ? UsageCategory.InboundTollFree : UsageCategory.InboundLocal,
        "outbound" or "callback" when BillableNumber.IsPstn(c.CallerId) => UsageCategory.Outbound,
        _ => null,
    };

    public void Add(MeteredCall c)
    {
        if (Categorize(c) is not { } category) { Internal++; return; }
        if (c.StartedAt is not { } start || c.DisconnectedAt is not { } end)
        {
            if (c.Closed) NeedsReview++; else Unended++;
            return;
        }

        var seconds = Math.Max(0, (long)Math.Round((end - start).TotalSeconds));
        var line = category switch
        {
            UsageCategory.InboundLocal => InboundLocal,
            UsageCategory.InboundTollFree => InboundTollFree,
            _ => Outbound,
        };
        line.AddCall(seconds);

        if (category != UsageCategory.Outbound)
        {
            var number = BillableNumber.Nanp(c.Dnis)!;
            if (!ByNumber.TryGetValue(number, out var n)) ByNumber[number] = n = new UsageLine();
            n.AddCall(seconds);
        }
    }
}

/// <summary>The bill for a period: every minute at the base rate, toll-free minutes plus the surcharge, raised to the monthly
/// minimum when usage falls short. Each line (local, toll-free, outbound) is rounded to the cent on its own and the total is
/// the sum of those rounded lines (S179) — the same numbers an invoice shows, so the lines always add up to the total.</summary>
public sealed record UsageCharges(decimal Local, decimal TollFree, decimal Outbound, decimal Usage, decimal Minimum, decimal Total)
{
    /// <summary>Local inbound + outbound — the base-rate minutes (kept for the Portal usage card).</summary>
    public decimal LocalAndOutbound => Local + Outbound;

    public static decimal Line(long billedMinutes, decimal pricePerMinute) =>
        Math.Round(billedMinutes * pricePerMinute, 2, MidpointRounding.AwayFromZero);

    public static UsageCharges Calculate(UsageTally t, decimal ratePerMinute, decimal tollFreeSurcharge, decimal monthlyMinimum)
    {
        var local = Line(t.InboundLocal.BilledMinutes, ratePerMinute);
        var tollFree = Line(t.InboundTollFree.BilledMinutes, ratePerMinute + tollFreeSurcharge);
        var outbound = Line(t.Outbound.BilledMinutes, ratePerMinute);
        var usage = local + tollFree + outbound;
        return new UsageCharges(local, tollFree, outbound, usage, monthlyMinimum, Math.Max(usage, monthlyMinimum));
    }
}

/// <summary>A month's usage as invoice lines (S179): one line per category with billed minutes (rates copied on), plus a
/// monthly-minimum top-up when usage falls short. Same arithmetic as <see cref="UsageCharges"/>.</summary>
public static class InvoiceUsageLines
{
    public sealed record Line(string Kind, string Description, decimal Quantity, decimal UnitPrice);

    public static IReadOnlyList<Line> Build(UsageTally t, decimal ratePerMinute, decimal tollFreeSurcharge, decimal monthlyMinimum, string periodLabel)
    {
        var lines = new List<Line>();
        if (t.InboundLocal.BilledMinutes > 0)
            lines.Add(new("usage_local", $"Inbound calls, local numbers — {periodLabel} ({t.InboundLocal.Calls:N0} calls)",
                t.InboundLocal.BilledMinutes, ratePerMinute));
        if (t.InboundTollFree.BilledMinutes > 0)
            lines.Add(new("usage_tollfree", $"Inbound calls, toll-free numbers — {periodLabel} ({t.InboundTollFree.Calls:N0} calls)",
                t.InboundTollFree.BilledMinutes, ratePerMinute + tollFreeSurcharge));
        if (t.Outbound.BilledMinutes > 0)
            lines.Add(new("usage_outbound", $"Outbound calls — {periodLabel} ({t.Outbound.Calls:N0} calls)",
                t.Outbound.BilledMinutes, ratePerMinute));

        var usage = UsageCharges.Calculate(t, ratePerMinute, tollFreeSurcharge, monthlyMinimum).Usage;
        if (monthlyMinimum > usage)
            lines.Add(new("minimum", $"Monthly minimum {monthlyMinimum:C} — usage {usage:C}", 1, monthlyMinimum - usage));
        return lines;
    }
}
