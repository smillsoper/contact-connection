using ContactConnection.Api.Endpoints;
using Xunit;

namespace ContactConnection.Api.Tests;

public class ExportWindowTests
{
    [Fact]
    public void Window_IsWholeLocalDays_InTheExportZone()
    {
        Assert.True(ExportsEndpoints.TryWindow(new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 4), "America/New_York", out var s, out var e, out _));
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 4, 0, 0, TimeSpan.Zero), s);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 4, 0, 0, TimeSpan.Zero), e);
        // The day the clocks fall back is 25 hours long.
        Assert.True(ExportsEndpoints.TryWindow(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), "America/New_York", out s, out e, out _));
        Assert.Equal(TimeSpan.FromHours(25), e - s);
        Assert.False(ExportsEndpoints.TryWindow(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 4), "America/New_York", out _, out _, out _));
    }
}
