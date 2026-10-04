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

/// <summary>One call as the meter sees it. <c>EndedAt</c> is when the caller's leg ended (terminal call state), else the
/// record's end.</summary>
public sealed record MeteredCall(string Source, string? CallerId, string? Dnis, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt);

public sealed class UsageLine
{
    public int Calls { get; internal set; }
    public long Seconds { get; internal set; }
    public decimal Minutes => Math.Round(Seconds / 60m, 2);
}

public sealed class UsageTally
{
    public UsageLine InboundLocal { get; } = new();
    public UsageLine InboundTollFree { get; } = new();
    public UsageLine Outbound { get; } = new();

    /// <summary>Inbound minutes per number dialed, for checking against the carrier's per-number usage.</summary>
    public Dictionary<string, UsageLine> ByNumber { get; } = [];

    /// <summary>Calls that crossed the PSTN but have no end time yet (still live, or never closed out).</summary>
    public int Unended { get; private set; }

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
        if (c.StartedAt is not { } start || c.EndedAt is not { } end) { Unended++; return; }

        var seconds = Math.Max(0, (long)Math.Round((end - start).TotalSeconds));
        var line = category switch
        {
            UsageCategory.InboundLocal => InboundLocal,
            UsageCategory.InboundTollFree => InboundTollFree,
            _ => Outbound,
        };
        line.Calls++;
        line.Seconds += seconds;

        if (category != UsageCategory.Outbound)
        {
            var number = BillableNumber.Nanp(c.Dnis)!;
            if (!ByNumber.TryGetValue(number, out var n)) ByNumber[number] = n = new UsageLine();
            n.Calls++;
            n.Seconds += seconds;
        }
    }
}

/// <summary>The bill for a period: every minute at the base rate, toll-free minutes plus the surcharge, raised to the monthly
/// minimum when usage falls short.</summary>
public sealed record UsageCharges(decimal LocalAndOutbound, decimal TollFree, decimal Usage, decimal Minimum, decimal Total)
{
    public static UsageCharges Calculate(UsageTally t, decimal ratePerMinute, decimal tollFreeSurcharge, decimal monthlyMinimum)
    {
        var local = Math.Round((t.InboundLocal.Minutes + t.Outbound.Minutes) * ratePerMinute, 2, MidpointRounding.AwayFromZero);
        var tollFree = Math.Round(t.InboundTollFree.Minutes * (ratePerMinute + tollFreeSurcharge), 2, MidpointRounding.AwayFromZero);
        var usage = local + tollFree;
        return new UsageCharges(local, tollFree, usage, monthlyMinimum, Math.Max(usage, monthlyMinimum));
    }
}
