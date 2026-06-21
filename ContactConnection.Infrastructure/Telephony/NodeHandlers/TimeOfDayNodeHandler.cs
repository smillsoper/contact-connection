using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class TimeOfDayNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_time_of_day";

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var timezone = node["timezone"]?.GetValue<string>() ?? "UTC";
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { tz = TimeZoneInfo.Utc; }

        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        var dayOfWeek = (int)now.DayOfWeek; // 0=Sun, 6=Sat
        var timeStr = now.ToString("HH:mm");

        var windows = node["windows"]?.AsArray();
        string? matchedWindow = null;

        // Evaluate in priority order: date overrides → holidays → weekly schedules
        if (windows is not null)
        {
            // Pass 1: specific date overrides (highest priority)
            foreach (var window in windows)
            {
                if (window is not JsonObject w) continue;
                if ((w["windowType"]?.GetValue<string>() ?? "weekly") != "date") continue;
                var name = w["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name)) continue;
                var dateStr = w["date"]?.GetValue<string>();
                if (string.IsNullOrEmpty(dateStr)) continue;
                if (!DateOnly.TryParse(dateStr, out var target)) continue;
                if (DateOnly.FromDateTime(now.Date) != target) continue;
                var start = w["start"]?.GetValue<string>() ?? "00:00";
                var end = w["end"]?.GetValue<string>() ?? "23:59";
                if (IsTimeInRange(timeStr, start, end)) { matchedWindow = name; break; }
            }

            // Pass 2: holiday windows
            if (matchedWindow is null)
            {
                foreach (var window in windows)
                {
                    if (window is not JsonObject w) continue;
                    if ((w["windowType"]?.GetValue<string>() ?? "weekly") != "holiday") continue;
                    var name = w["name"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(name)) continue;
                    var holidayKey = w["holiday"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(holidayKey) && IsHoliday(holidayKey, now))
                    {
                        var start = w["start"]?.GetValue<string>() ?? "00:00";
                        var end = w["end"]?.GetValue<string>() ?? "23:59";
                        if (IsTimeInRange(timeStr, start, end)) { matchedWindow = name; break; }
                    }
                }
            }

            // Pass 3: weekly schedules
            if (matchedWindow is null)
            {
                foreach (var window in windows)
                {
                    if (window is not JsonObject w) continue;
                    var wt = w["windowType"]?.GetValue<string>() ?? "weekly";
                    if (wt != "weekly") continue;
                    var name = w["name"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(name)) continue;
                    var days = w["days"]?.AsArray()?.Select(d => d?.GetValue<int>()).OfType<int>().ToHashSet();
                    if (days is not null && !days.Contains(dayOfWeek)) continue;
                    var start = w["start"]?.GetValue<string>() ?? "00:00";
                    var end = w["end"]?.GetValue<string>() ?? "24:00";
                    if (IsTimeInRange(timeStr, start, end)) { matchedWindow = name; break; }
                }
            }
        }

        var transition = matchedWindow ?? "no_match";
        var nextNodeId = node["transitions"]?[transition]?.GetValue<string>()
                      ?? node["transitions"]?["default"]?.GetValue<string>();

        return Task.FromResult(new TelephonyNodeResult(nextNodeId, transition));
    }

    private static bool IsTimeInRange(string current, string start, string end)
    {
        // Handles overnight ranges (e.g. start=22:00, end=06:00)
        if (string.Compare(start, end, StringComparison.Ordinal) < 0)
            return string.Compare(current, start, StringComparison.Ordinal) >= 0
                && string.Compare(current, end, StringComparison.Ordinal) < 0;
        else
            return string.Compare(current, start, StringComparison.Ordinal) >= 0
                || string.Compare(current, end, StringComparison.Ordinal) < 0;
    }

    private static bool IsHoliday(string holiday, DateTimeOffset now)
    {
        var date = now.Date;
        int year = date.Year;

        return holiday switch
        {
            "new_years"       => date.Month == 1  && date.Day == 1,
            "mlk_day"         => date == NthWeekdayOfMonth(year, 1,  DayOfWeek.Monday,   3),
            "presidents_day"  => date == NthWeekdayOfMonth(year, 2,  DayOfWeek.Monday,   3),
            "memorial_day"    => date == LastWeekdayOfMonth(year, 5, DayOfWeek.Monday),
            "juneteenth"      => date.Month == 6  && date.Day == 19,
            "independence_day"=> date.Month == 7  && date.Day == 4,
            "labor_day"       => date == NthWeekdayOfMonth(year, 9,  DayOfWeek.Monday,   1),
            "columbus_day"    => date == NthWeekdayOfMonth(year, 10, DayOfWeek.Monday,   2),
            "veterans_day"    => date.Month == 11 && date.Day == 11,
            "thanksgiving"    => date == NthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4),
            "christmas_eve"   => date.Month == 12 && date.Day == 24,
            "christmas"       => date.Month == 12 && date.Day == 25,
            "new_years_eve"   => date.Month == 12 && date.Day == 31,
            _                 => false,
        };
    }

    private static DateTime NthWeekdayOfMonth(int year, int month, DayOfWeek dow, int n)
    {
        var first = new DateTime(year, month, 1);
        int offset = ((int)dow - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + (n - 1) * 7);
    }

    private static DateTime LastWeekdayOfMonth(int year, int month, DayOfWeek dow)
    {
        var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        int offset = ((int)last.DayOfWeek - (int)dow + 7) % 7;
        return last.AddDays(-offset);
    }
}
