using ContactConnection.Api.Endpoints;
using Xunit;

namespace ContactConnection.Api.Tests;

public class KpiWindowTests
{
    // Wednesday 2026-10-07 15:00 UTC = 08:00 PDT.
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("today", "2026-10-07T07:00:00+00:00", "2026-10-07T15:00:00+00:00")]
    [InlineData("yesterday", "2026-10-06T07:00:00+00:00", "2026-10-07T07:00:00+00:00")]
    [InlineData("week", "2026-10-05T07:00:00+00:00", "2026-10-07T15:00:00+00:00")]   // Monday
    [InlineData("month", "2026-10-01T07:00:00+00:00", "2026-10-07T15:00:00+00:00")]
    public void CalendarWindows_InTheTenantZone(string mode, string since, string until)
    {
        var (s, u) = KpiEndpoints.Window("America/Los_Angeles", mode, null, Now);
        Assert.Equal(DateTimeOffset.Parse(since), s);
        Assert.Equal(DateTimeOffset.Parse(until), u);
    }

    [Fact]
    public void RollingWindows()
    {
        Assert.Equal(Now.AddHours(-4), KpiEndpoints.Window("America/Los_Angeles", "hours", 4, Now).Since);
        Assert.Equal(Now.AddMinutes(-15), KpiEndpoints.Window("America/Los_Angeles", "minutes", 15, Now).Since);
    }

    [Theory]
    [InlineData("today", -1)]
    [InlineData("week", -7)]
    public void Previous_period_is_the_same_span_earlier(string mode, int days)
    {
        var (since, until) = KpiEndpoints.Window("America/Los_Angeles", mode, null, Now);
        var (ps, pu) = KpiEndpoints.PreviousWindow(mode, since, until);
        Assert.Equal(since.AddDays(days), ps);
        Assert.Equal(until.AddDays(days), pu);
    }

    [Fact]
    public void Previous_moving_window_is_the_window_before()
    {
        var (since, until) = KpiEndpoints.Window("UTC", "hours", 2, Now);
        var (ps, pu) = KpiEndpoints.PreviousWindow("hours", since, until);
        Assert.Equal(since.AddHours(-2), ps);
        Assert.Equal(since, pu);
    }
}
