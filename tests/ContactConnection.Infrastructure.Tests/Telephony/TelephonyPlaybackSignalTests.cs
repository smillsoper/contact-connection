using System.Diagnostics;
using ContactConnection.Infrastructure.Telephony;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

public class TelephonyPlaybackSignalTests
{
    [Fact]
    public async Task Signal_BeforeWait_IsLatched_AndConsumedOnce()
    {
        var sut = new TelephonyPlaybackSignal();
        sut.Signal("uuid-1");

        Assert.True(await sut.WaitAsync("uuid-1", TimeSpan.FromSeconds(1)));

        // Latch is single-use — a second wait now times out.
        Assert.False(await sut.WaitAsync("uuid-1", TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task Signal_WhileWaiting_ReleasesTheWaiter()
    {
        var sut = new TelephonyPlaybackSignal();
        var wait = sut.WaitAsync("uuid-2", TimeSpan.FromSeconds(5));

        await Task.Delay(50);
        sut.Signal("uuid-2");

        Assert.True(await wait);
    }

    [Fact]
    public async Task Wait_Timeout_ReturnsFalse_Promptly()
    {
        var sut = new TelephonyPlaybackSignal();
        var sw = Stopwatch.StartNew();

        var result = await sut.WaitAsync("uuid-3", TimeSpan.FromMilliseconds(200));

        Assert.False(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task Signal_IsPerChannel()
    {
        var sut = new TelephonyPlaybackSignal();
        sut.Signal("uuid-a");

        Assert.False(await sut.WaitAsync("uuid-b", TimeSpan.FromMilliseconds(100)));
        Assert.True(await sut.WaitAsync("uuid-a", TimeSpan.FromMilliseconds(100)));
    }
}
