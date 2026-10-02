namespace ContactConnection.Domain.ValueObjects;

/// <summary>A pay period: [Start, End] inclusive, as local dates in the tenant's time zone.</summary>
public readonly record struct PayPeriod(DateOnly Start, DateOnly End)
{
    public PayPeriod Previous(string frequency, DateOnly anchor) => PayPeriods.Containing(Start.AddDays(-1), frequency, anchor);
    public PayPeriod Next(string frequency, DateOnly anchor) => PayPeriods.Containing(End.AddDays(1), frequency, anchor);
    public string Label => $"{Start:MMM d} – {End:MMM d, yyyy}";
}

/// <summary>Commission pay-period math (S171) — pure, in tenant-local dates.</summary>
public static class PayPeriods
{
    public const string Weekly      = "weekly";
    public const string Biweekly    = "biweekly";
    public const string Semimonthly = "semimonthly";   // 1st–15th, 16th–end of month
    public const string Monthly     = "monthly";

    public static readonly DateOnly DefaultAnchor = new(2026, 1, 5);   // a Monday

    public static bool IsValid(string? f) => f is Weekly or Biweekly or Semimonthly or Monthly;

    public static DateOnly AnchorOf(string? start) =>
        DateOnly.TryParse(start, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : DefaultAnchor;

    public static PayPeriod Containing(DateOnly date, string? frequency, DateOnly anchor)
    {
        switch (frequency)
        {
            case Semimonthly:
                return date.Day <= 15
                    ? new(new DateOnly(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, 15))
                    : new(new DateOnly(date.Year, date.Month, 16),
                          new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)));
            case Monthly:
                var first = new DateOnly(date.Year, date.Month, 1);
                return new(first, first.AddMonths(1).AddDays(-1));
            default:
                var days = frequency == Weekly ? 7 : 14;
                var offset = date.DayNumber - anchor.DayNumber;
                var index = (int)Math.Floor(offset / (double)days);
                var start = anchor.AddDays(index * days);
                return new(start, start.AddDays(days - 1));
        }
    }
}
