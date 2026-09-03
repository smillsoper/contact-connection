using System.Globalization;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// Shared date/time resolution for the <c>tf_scheduled_callback</c> (telephony) and
/// <c>scheduled_callback</c> (CRM script) nodes. The tenant's flow supplies the date and time as
/// free text (already <c>{{variable}}</c>-resolved by the caller); this parses them in the tenant
/// timezone and validates against an optional allowed day/time-of-day window.
/// </summary>
public static class ScheduledCallbackTimeParser
{
    public readonly record struct AllowedWindow(string? Days, string? StartTime, string? EndTime);

    /// <summary>Outcome codes match the node transitions.</summary>
    public const string Ok          = "ok";
    public const string Failed      = "failed";       // unparseable / no date
    public const string InvalidTime = "invalid_time"; // parsed but past / outside the window

    /// <returns>(when, "ok") when valid; (null, "failed") when unparseable; (null, "invalid_time")
    /// when parsed but in the past or outside <paramref name="window"/>.</returns>
    public static (DateTimeOffset? When, string Outcome) Resolve(
        string? dateRaw, string? timeRaw, string tenantTimezone, AllowedWindow window)
    {
        dateRaw = dateRaw?.Trim();
        timeRaw = string.IsNullOrWhiteSpace(timeRaw) ? "09:00" : timeRaw.Trim();

        if (string.IsNullOrWhiteSpace(dateRaw)) return (null, Failed);

        var combined = $"{dateRaw} {timeRaw}";
        if (!DateTime.TryParse(combined, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out var localNaive)
            && !DateTime.TryParse(combined, CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces, out localNaive))
        {
            return (null, Failed);
        }

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(tenantTimezone); }
        catch { tz = TimeZoneInfo.Utc; }

        var local = DateTime.SpecifyKind(localNaive, DateTimeKind.Unspecified);
        var when  = new DateTimeOffset(local, tz.GetUtcOffset(local));

        if (when <= DateTimeOffset.UtcNow) return (null, InvalidTime);

        var allowedDays = (window.Days ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var d) ? d : -1)
            .Where(d => d is >= 0 and <= 6)
            .ToHashSet();
        if (allowedDays.Count > 0 && !allowedDays.Contains((int)local.DayOfWeek))
            return (null, InvalidTime);

        var tod = local.TimeOfDay;
        if (TimeSpan.TryParse(window.StartTime, out var start) && tod < start) return (null, InvalidTime);
        if (TimeSpan.TryParse(window.EndTime, out var end) && tod > end) return (null, InvalidTime);

        return (when, Ok);
    }
}
