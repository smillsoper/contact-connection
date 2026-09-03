using ContactConnection.Infrastructure.Telephony;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>
/// ScheduledCallbackTimeParser — shared by the tf_scheduled_callback (telephony) and
/// scheduled_callback (CRM) nodes: parse a free-text date/time in the tenant timezone and
/// validate it against an optional allowed day/time-of-day window.
/// </summary>
public class ScheduledCallbackTimeParserTests
{
    private static readonly ScheduledCallbackTimeParser.AllowedWindow None = new(null, null, null);
    private const string Tz = "America/Chicago";

    private static string FutureDate(int days = 3) => DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-dd");

    [Fact]
    public void ValidFuture_ReturnsOk_WithFutureInstant()
    {
        var (when, outcome) = ScheduledCallbackTimeParser.Resolve(FutureDate(), "14:00", Tz, None);
        Assert.Equal(ScheduledCallbackTimeParser.Ok, outcome);
        Assert.True(when > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void BlankTime_DefaultsTo0900()
    {
        var (when, outcome) = ScheduledCallbackTimeParser.Resolve(FutureDate(), "", Tz, None);
        Assert.Equal(ScheduledCallbackTimeParser.Ok, outcome);
        Assert.NotNull(when);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("next thursday afternoon")]
    public void UnparseableOrBlankDate_Fails(string? date)
    {
        var (_, outcome) = ScheduledCallbackTimeParser.Resolve(date, "10:00", Tz, None);
        Assert.Equal(ScheduledCallbackTimeParser.Failed, outcome);
    }

    [Fact]
    public void PastInstant_InvalidTime()
    {
        var (_, outcome) = ScheduledCallbackTimeParser.Resolve(
            DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"), "10:00", Tz, None);
        Assert.Equal(ScheduledCallbackTimeParser.InvalidTime, outcome);
    }

    [Fact]
    public void OutsideAllowedHours_InvalidTime()
    {
        var window = new ScheduledCallbackTimeParser.AllowedWindow(null, "08:00", "12:00");
        var (_, outcome) = ScheduledCallbackTimeParser.Resolve(FutureDate(), "14:00", Tz, window);
        Assert.Equal(ScheduledCallbackTimeParser.InvalidTime, outcome);
    }

    [Fact]
    public void InsideAllowedHours_Ok()
    {
        var window = new ScheduledCallbackTimeParser.AllowedWindow(null, "08:00", "17:00");
        var (_, outcome) = ScheduledCallbackTimeParser.Resolve(FutureDate(), "10:30", Tz, window);
        Assert.Equal(ScheduledCallbackTimeParser.Ok, outcome);
    }

    [Fact]
    public void DayNotInAllowedDays_InvalidTime()
    {
        // Find a future date and forbid its weekday.
        var d = DateTime.UtcNow.AddDays(3);
        var forbidAllButNot = string.Join(",", Enumerable.Range(0, 7).Where(x => x != (int)d.DayOfWeek));
        var window = new ScheduledCallbackTimeParser.AllowedWindow(forbidAllButNot, null, null);
        var (_, outcome) = ScheduledCallbackTimeParser.Resolve(d.ToString("yyyy-MM-dd"), "10:00", Tz, window);
        Assert.Equal(ScheduledCallbackTimeParser.InvalidTime, outcome);
    }

    [Fact]
    public void UnknownTimezone_FallsBackToUtc_StillParses()
    {
        var (when, outcome) = ScheduledCallbackTimeParser.Resolve(FutureDate(), "12:00", "Not/AZone", None);
        Assert.Equal(ScheduledCallbackTimeParser.Ok, outcome);
        Assert.Equal(TimeSpan.Zero, when!.Value.Offset);
    }
}
