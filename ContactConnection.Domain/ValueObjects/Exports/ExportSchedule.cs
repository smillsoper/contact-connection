namespace ContactConnection.Domain.ValueObjects.Exports;

/// <summary>
/// When an export runs, and which calls each run covers (S180, Export Worker session 2). The two are deliberately separate,
/// each in its own time zone: Cannella's files run at <b>9:30 PM Pacific</b> and each covers <b>the previous day in
/// Eastern</b> — at 9:30 PM Pacific it's already 12:30 AM Eastern, so "previous day" is the Eastern day that just ended.
///
/// Not part of <see cref="ExportSpec"/>: the vendor approves what's in the file, not when it's sent, so changing the
/// schedule doesn't mark the export "changed since vendor approval".
/// </summary>
public sealed record ExportSchedule
{
    /// <summary><c>daily</c> (optionally only on <see cref="DaysOfWeek"/>) or <c>monthly</c> (on <see cref="DayOfMonth"/>).</summary>
    public string Frequency { get; init; } = ExportFrequency.Daily;
    /// <summary>Daily only: 0 = Sunday … 6 = Saturday. Empty = every day.</summary>
    public List<int> DaysOfWeek { get; init; } = [];
    /// <summary>Monthly only: 1–28.</summary>
    public int DayOfMonth { get; init; } = 1;
    /// <summary>"HH:mm", 24-hour, in <see cref="TimeZone"/>.</summary>
    public string TimeOfDay { get; init; } = "02:00";
    /// <summary>IANA zone the run time is in.</summary>
    public string TimeZone { get; init; } = "America/Los_Angeles";

    /// <summary>Which calls a run covers — see <see cref="ExportWindowKind"/>. Days/weeks/months are whole periods in the
    /// export's own time zone (<see cref="ExportSpec.TimeZone"/>).</summary>
    public string Window { get; init; } = ExportWindowKind.PreviousDay;
    /// <summary><see cref="ExportWindowKind.LastHours"/> only.</summary>
    public int LastHours { get; init; } = 24;

    /// <summary>Send each scheduled file to the export's delivery targets as soon as it's generated.</summary>
    public bool AutoDeliver { get; init; } = true;
}

public static class ExportFrequency
{
    public const string Daily = "daily";
    public const string Monthly = "monthly";
    public static bool IsValid(string? v) => v is Daily or Monthly;
}

public static class ExportWindowKind
{
    public const string PreviousDay = "previous_day";
    /// <summary>The last full Monday–Sunday week.</summary>
    public const string PreviousWeek = "previous_week";
    public const string PreviousMonth = "previous_month";
    public const string LastHours = "last_hours";
    /// <summary>From where the last real (non-test) file ended up to the run time — nothing missed, nothing twice.</summary>
    public const string SinceLastRun = "since_last_run";
    public static bool IsValid(string? v) => v is PreviousDay or PreviousWeek or PreviousMonth or LastHours or SinceLastRun;
}

/// <summary>Pure schedule maths — no clock, no I/O — so every case (DST, month ends, missed runs) is unit-tested.</summary>
public static class ExportScheduleCalculator
{
    /// <summary>First problem with the schedule, or null.</summary>
    public static string? Validate(ExportSchedule s)
    {
        if (!ExportFrequency.IsValid(s.Frequency)) return $"Unknown frequency '{s.Frequency}'.";
        if (!ExportWindowKind.IsValid(s.Window)) return $"Unknown window '{s.Window}'.";
        if (ParseTime(s.TimeOfDay) is null) return "The run time must be HH:mm (24-hour).";
        if (Zone(s.TimeZone) is null) return $"Unknown time zone '{s.TimeZone}'.";
        if (s.DaysOfWeek.Any(d => d is < 0 or > 6)) return "Days of the week are 0 (Sunday) to 6 (Saturday).";
        if (s.Frequency == ExportFrequency.Monthly && s.DayOfMonth is < 1 or > 28) return "The day of the month must be 1 to 28.";
        if (s.Window == ExportWindowKind.LastHours && s.LastHours is < 1 or > 24 * 31) return "Last N hours must be 1 to 744.";
        return null;
    }

    /// <summary>Run times strictly after <paramref name="after"/> and no later than <paramref name="until"/>, oldest first.</summary>
    public static IEnumerable<DateTimeOffset> Occurrences(ExportSchedule s, DateTimeOffset after, DateTimeOffset until)
    {
        var zone = Zone(s.TimeZone) ?? TimeZoneInfo.Utc;
        var time = ParseTime(s.TimeOfDay) ?? new TimeOnly(2, 0);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(until, zone).DateTime);
        for (; day <= lastDay; day = day.AddDays(1))
        {
            if (!RunsOn(s, day)) continue;
            var at = ToInstant(day, time, zone);
            if (at > after && at <= until) yield return at;
        }
    }

    /// <summary>The next <paramref name="count"/> run times after <paramref name="after"/>.</summary>
    public static IReadOnlyList<DateTimeOffset> Next(ExportSchedule s, DateTimeOffset after, int count) =>
        Occurrences(s, after, after.AddDays(400)).Take(count).ToList();

    /// <summary>The calls a run at <paramref name="runAt"/> covers: [start, end) in UTC.
    /// <paramref name="lastRunEnd"/>: where the last real file's window ended (since-last-run only).</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Window(
        ExportSchedule s, string dataTimeZone, DateTimeOffset runAt, DateTimeOffset? lastRunEnd)
    {
        var zone = Zone(dataTimeZone) ?? TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(runAt, zone).DateTime);
        switch (s.Window)
        {
            case ExportWindowKind.PreviousWeek:
                // Monday of this week, then back one week.
                var sinceMonday = ((int)today.DayOfWeek + 6) % 7;
                var thisMonday = today.AddDays(-sinceMonday);
                return (Midnight(thisMonday.AddDays(-7), zone), Midnight(thisMonday, zone));
            case ExportWindowKind.PreviousMonth:
                var first = new DateOnly(today.Year, today.Month, 1);
                return (Midnight(first.AddMonths(-1), zone), Midnight(first, zone));
            case ExportWindowKind.LastHours:
                return (runAt.AddHours(-s.LastHours).ToUniversalTime(), runAt.ToUniversalTime());
            case ExportWindowKind.SinceLastRun:
                var start = lastRunEnd is { } e && e < runAt ? e : runAt.AddDays(-1);
                return (start.ToUniversalTime(), runAt.ToUniversalTime());
            default:
                return (Midnight(today.AddDays(-1), zone), Midnight(today, zone));
        }
    }

    /// <summary>"Every day at 9:30 PM (America/Los_Angeles) — each file covers the previous day in America/New_York."</summary>
    public static string Describe(ExportSchedule s, string dataTimeZone)
    {
        var time = ParseTime(s.TimeOfDay) ?? new TimeOnly(2, 0);
        var when = s.Frequency == ExportFrequency.Monthly
            ? $"On day {s.DayOfMonth} of every month"
            : s.DaysOfWeek.Count is 0 or 7
                ? "Every day"
                : "Every " + string.Join(", ", s.DaysOfWeek.Order().Select(d => ((DayOfWeek)d).ToString()));
        var covers = s.Window switch
        {
            ExportWindowKind.PreviousWeek => $"the previous Monday–Sunday week in {dataTimeZone}",
            ExportWindowKind.PreviousMonth => $"the previous month in {dataTimeZone}",
            ExportWindowKind.LastHours => $"the {s.LastHours} hours before it runs",
            ExportWindowKind.SinceLastRun => "everything since the last file",
            _ => $"the previous day in {dataTimeZone}",
        };
        return $"{when} at {time.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)} ({s.TimeZone}) — each file covers {covers}.";
    }

    private static bool RunsOn(ExportSchedule s, DateOnly day) => s.Frequency == ExportFrequency.Monthly
        ? day.Day == s.DayOfMonth
        : s.DaysOfWeek.Count == 0 || s.DaysOfWeek.Contains((int)day.DayOfWeek);

    /// <summary>A local wall-clock time as an instant. A time skipped by spring-forward runs an hour later; a repeated
    /// fall-back time runs at its first occurrence.</summary>
    private static DateTimeOffset ToInstant(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo zone) => ToInstant(day, TimeOnly.MinValue, zone);

    private static TimeOnly? ParseTime(string? s) =>
        TimeOnly.TryParseExact(s, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) ? t : null;

    public static TimeZoneInfo? Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }
}
