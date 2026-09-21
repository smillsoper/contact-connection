using System.Reflection;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_time_of_day had zero coverage despite a 3-pass priority evaluator (date override → holiday →
/// weekly schedule) and several date-math edge cases (overnight time ranges, Nth-weekday-of-month
/// and last-weekday-of-month holiday calculations) that are exactly the kind of thing that looks
/// right until a specific year's calendar disagrees. The pure calculation helpers (IsTimeInRange,
/// IsHoliday, and by extension NthWeekdayOfMonth/LastWeekdayOfMonth) are private static with no
/// time-provider seam, so they're exercised via reflection; holiday dates are cross-checked against
/// an independently-implemented (LINQ scan, not offset arithmetic) reference calculation across
/// multiple years rather than hardcoded "trust me" calendar dates.
/// </summary>
public class TimeOfDayNodeHandlerTests
{
    private static readonly TimeOfDayNodeHandler Handler = new();

    private static bool InvokeIsTimeInRange(string current, string start, string end)
    {
        var m = typeof(TimeOfDayNodeHandler).GetMethod("IsTimeInRange", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)m.Invoke(null, [current, start, end])!;
    }

    private static bool InvokeIsHoliday(string holiday, DateTimeOffset now)
    {
        var m = typeof(TimeOfDayNodeHandler).GetMethod("IsHoliday", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)m.Invoke(null, [holiday, now])!;
    }

    private static DateTime ReferenceNthWeekday(int year, int month, DayOfWeek dow, int n) =>
        Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Select(d => new DateTime(year, month, d))
            .Where(d => d.DayOfWeek == dow)
            .ElementAt(n - 1);

    private static DateTime ReferenceLastWeekday(int year, int month, DayOfWeek dow) =>
        Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Select(d => new DateTime(year, month, d))
            .Where(d => d.DayOfWeek == dow)
            .Last();

    // ── IsTimeInRange ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("09:00", "08:00", "17:00", true)]
    [InlineData("08:00", "08:00", "17:00", true)]   // start boundary inclusive
    [InlineData("17:00", "08:00", "17:00", false)]  // end boundary exclusive
    [InlineData("07:59", "08:00", "17:00", false)]
    [InlineData("17:01", "08:00", "17:00", false)]
    public void IsTimeInRange_NormalDaytimeRange(string current, string start, string end, bool expected) =>
        Assert.Equal(expected, InvokeIsTimeInRange(current, start, end));

    [Theory]
    [InlineData("23:00", "22:00", "06:00", true)]   // before midnight
    [InlineData("02:00", "22:00", "06:00", true)]   // after midnight
    [InlineData("22:00", "22:00", "06:00", true)]   // start boundary
    [InlineData("05:59", "22:00", "06:00", true)]
    [InlineData("06:00", "22:00", "06:00", false)]  // end boundary exclusive, wraps to "less than"
    [InlineData("12:00", "22:00", "06:00", false)]  // midday, outside the overnight window
    public void IsTimeInRange_OvernightWraparound(string current, string start, string end, bool expected) =>
        Assert.Equal(expected, InvokeIsTimeInRange(current, start, end));

    // ── IsHoliday — fixed calendar-date holidays ───────────────────────────────

    [Theory]
    [InlineData("new_years", 1, 1)]
    [InlineData("juneteenth", 6, 19)]
    [InlineData("independence_day", 7, 4)]
    [InlineData("veterans_day", 11, 11)]
    [InlineData("christmas_eve", 12, 24)]
    [InlineData("christmas", 12, 25)]
    [InlineData("new_years_eve", 12, 31)]
    public void IsHoliday_FixedDate_MatchesOnlyThatDate(string key, int month, int day)
    {
        Assert.True(InvokeIsHoliday(key, new DateTimeOffset(2026, month, day, 12, 0, 0, TimeSpan.Zero)));
        Assert.False(InvokeIsHoliday(key, new DateTimeOffset(2026, month, day, 12, 0, 0, TimeSpan.Zero).AddDays(-1)));
        Assert.False(InvokeIsHoliday(key, new DateTimeOffset(2026, month, day, 12, 0, 0, TimeSpan.Zero).AddDays(1)));
    }

    [Fact]
    public void IsHoliday_UnknownKey_AlwaysFalse()
    {
        Assert.False(InvokeIsHoliday("arbor_day", DateTimeOffset.UtcNow));
    }

    // ── IsHoliday — Nth-weekday-of-month holidays, cross-checked across years ──

    [Theory]
    [InlineData("mlk_day", 1, DayOfWeek.Monday, 3)]
    [InlineData("presidents_day", 2, DayOfWeek.Monday, 3)]
    [InlineData("labor_day", 9, DayOfWeek.Monday, 1)]
    [InlineData("columbus_day", 10, DayOfWeek.Monday, 2)]
    [InlineData("thanksgiving", 11, DayOfWeek.Thursday, 4)]
    public void IsHoliday_NthWeekday_MatchesReferenceCalculation_AcrossYears(
        string key, int month, DayOfWeek dow, int n)
    {
        foreach (var year in new[] { 2024, 2025, 2026, 2027 })
        {
            var expected = ReferenceNthWeekday(year, month, dow, n);
            var expectedOffset = new DateTimeOffset(expected, TimeSpan.Zero).AddHours(12);

            Assert.True(InvokeIsHoliday(key, expectedOffset), $"{key} {year} expected match on {expected:yyyy-MM-dd}");
            Assert.False(InvokeIsHoliday(key, expectedOffset.AddDays(-1)), $"{key} {year}: day before should not match");
            Assert.False(InvokeIsHoliday(key, expectedOffset.AddDays(1)), $"{key} {year}: day after should not match");
            Assert.False(InvokeIsHoliday(key, expectedOffset.AddDays(-7)), $"{key} {year}: a week before (same weekday) should not match");
        }
    }

    [Fact]
    public void IsHoliday_MemorialDay_LastMondayOfMay_MatchesReferenceCalculation_AcrossYears()
    {
        foreach (var year in new[] { 2024, 2025, 2026, 2027 })
        {
            var expected = ReferenceLastWeekday(year, 5, DayOfWeek.Monday);
            var expectedOffset = new DateTimeOffset(expected, TimeSpan.Zero).AddHours(12);

            Assert.True(InvokeIsHoliday("memorial_day", expectedOffset), $"memorial_day {year} expected match on {expected:yyyy-MM-dd}");
            Assert.False(InvokeIsHoliday("memorial_day", expectedOffset.AddDays(7)), $"memorial_day {year}: the following Monday must not also match");
        }
    }

    // ── ExecuteAsync — priority ordering and transition wiring ─────────────────

    private static TelephonyFlowContext Ctx() => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551234567", DestinationNumber = "+15557654321",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
    };

    private static JsonObject WeeklyWindow(string name, IEnumerable<int> days, string start = "00:00", string end = "24:00") => new()
    {
        ["windowType"] = "weekly",
        ["name"] = name,
        ["days"] = new JsonArray(days.Select(d => (JsonNode)d).ToArray()),
        ["start"] = start,
        ["end"] = end,
    };

    private static readonly int[] AllDays = [0, 1, 2, 3, 4, 5, 6];

    [Fact]
    public async Task NoWindows_FollowsNoMatch_ThenDefault()
    {
        var node = new JsonObject
        {
            ["type"] = "tf_time_of_day",
            ["transitions"] = new JsonObject { ["default"] = "node_fallback" },
        };

        var result = await Handler.ExecuteAsync(node, Ctx());

        Assert.Equal("no_match", result.TransitionTaken);
        Assert.Equal("node_fallback", result.NextNodeId);
    }

    [Fact]
    public async Task WeeklySchedule_AllDaysAllHours_AlwaysMatches()
    {
        var node = new JsonObject
        {
            ["type"] = "tf_time_of_day",
            ["windows"] = new JsonArray { WeeklyWindow("open_hours", AllDays) },
            ["transitions"] = new JsonObject { ["open_hours"] = "node_open" },
        };

        var result = await Handler.ExecuteAsync(node, Ctx());

        Assert.Equal("open_hours", result.TransitionTaken);
        Assert.Equal("node_open", result.NextNodeId);
    }

    [Fact]
    public async Task WeeklySchedule_NoDaysArray_TreatedAsEveryDay()
    {
        // days omitted entirely — handler's null-check means "not restricted by day".
        var node = new JsonObject
        {
            ["type"] = "tf_time_of_day",
            ["windows"] = new JsonArray
            {
                new JsonObject { ["name"] = "always_open", ["start"] = "00:00", ["end"] = "24:00" },
            },
            ["transitions"] = new JsonObject { ["always_open"] = "node_open" },
        };

        var result = await Handler.ExecuteAsync(node, Ctx());
        Assert.Equal("always_open", result.TransitionTaken);
    }

    [Fact]
    public async Task WindowTypeDefaultsToWeekly_WhenOmitted()
    {
        var node = new JsonObject
        {
            ["type"] = "tf_time_of_day",
            ["windows"] = new JsonArray
            {
                new JsonObject { ["name"] = "default_type", ["days"] = new JsonArray(AllDays.Select(d => (JsonNode)d).ToArray()) },
            },
            ["transitions"] = new JsonObject { ["default_type"] = "node_matched" },
        };

        var result = await Handler.ExecuteAsync(node, Ctx());
        Assert.Equal("node_matched", result.NextNodeId);
    }

    [Fact]
    public async Task DateOverride_TakesPriorityOver_MatchingWeeklySchedule()
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var node = new JsonObject
        {
            ["type"] = "tf_time_of_day",
            ["windows"] = new JsonArray
            {
                WeeklyWindow("regular_hours", AllDays),
                new JsonObject
                {
                    ["windowType"] = "date",
                    ["name"] = "holiday_closure",
                    ["date"] = todayUtc.ToString("yyyy-MM-dd"),
                    ["start"] = "00:00",
                    ["end"] = "24:00",
                },
            },
            ["transitions"] = new JsonObject
            {
                ["regular_hours"] = "node_regular",
                ["holiday_closure"] = "node_closed",
            },
        };

        var result = await Handler.ExecuteAsync(node, Ctx());

        Assert.Equal("holiday_closure", result.TransitionTaken);
        Assert.Equal("node_closed", result.NextNodeId);
    }

    [Fact]
    public async Task DateOverride_ForDifferentDate_DoesNotSuppressWeeklyMatch()
    {
        var notToday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        var node = new JsonObject
        {
            ["type"] = "tf_time_of_day",
            ["windows"] = new JsonArray
            {
                new JsonObject
                {
                    ["windowType"] = "date",
                    ["name"] = "future_closure",
                    ["date"] = notToday.ToString("yyyy-MM-dd"),
                    ["start"] = "00:00",
                    ["end"] = "24:00",
                },
                WeeklyWindow("regular_hours", AllDays),
            },
            ["transitions"] = new JsonObject
            {
                ["future_closure"] = "node_closed",
                ["regular_hours"] = "node_regular",
            },
        };

        var result = await Handler.ExecuteAsync(node, Ctx());
        Assert.Equal("regular_hours", result.TransitionTaken);
    }

    [Fact]
    public async Task MatchedWindow_WithNoWiredTransition_FallsBackToDefault()
    {
        var node = new JsonObject
        {
            ["type"] = "tf_time_of_day",
            ["windows"] = new JsonArray { WeeklyWindow("open_hours", AllDays) },
            ["transitions"] = new JsonObject { ["default"] = "node_fallback" },
        };

        var result = await Handler.ExecuteAsync(node, Ctx());

        Assert.Equal("open_hours", result.TransitionTaken);
        Assert.Equal("node_fallback", result.NextNodeId);
    }

    [Fact]
    public async Task InvalidTimezone_FallsBackToUtc_DoesNotThrow()
    {
        var node = new JsonObject
        {
            ["type"] = "tf_time_of_day",
            ["timezone"] = "Not/ARealZone",
            ["windows"] = new JsonArray { WeeklyWindow("open_hours", AllDays) },
            ["transitions"] = new JsonObject { ["open_hours"] = "node_open" },
        };

        var result = await Handler.ExecuteAsync(node, Ctx());
        Assert.Equal("node_open", result.NextNodeId);
    }
}
