using ContactConnection.Infrastructure.Kpis;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Kpis;

/// <summary>S182 chart time axes: bucket labels sort in time order and empty periods are listed so they can be filled.</summary>
public class KpiTimeDimensionTests
{
    private static readonly DateTime At = new(2026, 10, 7, 14, 47, 0);   // a Wednesday

    [Theory]
    [InlineData(KpiDimension.Interval15, "2026-10-07 14:45")]
    [InlineData(KpiDimension.Interval30, "2026-10-07 14:30")]
    [InlineData(KpiDimension.Interval60, "2026-10-07 14:00")]
    [InlineData(KpiDimension.Week, "2026-10-05")]      // its Monday
    [InlineData(KpiDimension.Month, "2026-10")]
    [InlineData(KpiDimension.DayOfWeek, "Wed")]
    [InlineData(KpiDimension.Hour, "14:00")]
    public void Labels(string dim, string expected) => Assert.Equal(expected, KpiDimension.TimeLabel(dim, At));

    [Fact]
    public void Every_interval_in_the_window_is_listed()
    {
        var labels = KpiDimension.TimeLabels(KpiDimension.Interval30, new DateTime(2026, 10, 7, 0, 0, 0), new DateTime(2026, 10, 7, 2, 10, 0)).ToList();
        Assert.Equal(["2026-10-07 00:00", "2026-10-07 00:30", "2026-10-07 01:00", "2026-10-07 01:30", "2026-10-07 02:00"], labels);
        Assert.Equal(24, KpiDimension.TimeLabels(KpiDimension.Hour, At, At).Count());
        Assert.Equal(["2026-09", "2026-10"], KpiDimension.TimeLabels(KpiDimension.Month, new DateTime(2026, 9, 15), new DateTime(2026, 10, 2)));
    }

    [Fact]
    public void Days_of_the_week_sort_in_week_order()
    {
        var sorted = new[] { "Sun", "Wed", "Mon", "Fri" }.Order(KpiDimension.Order(KpiDimension.DayOfWeek));
        Assert.Equal(["Mon", "Wed", "Fri", "Sun"], sorted);
    }
}
