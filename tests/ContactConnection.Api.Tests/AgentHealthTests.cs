using ContactConnection.Api.Endpoints;
using Xunit;

namespace ContactConnection.Api.Tests;

/// <summary>Connection health scoring (S183): E-model MOS from round trip, jitter and loss → good / fair / poor.</summary>
public class AgentHealthTests
{
    [Fact]
    public void CleanLine_IsGood()
    {
        var mos = AgentHealthEndpoints.Mos(rttMs: 60, jitterMs: 5, lossPct: 0);
        Assert.True(mos >= 4.3, $"MOS {mos}");
        Assert.Equal("good", AgentHealthEndpoints.Grade(mos, micSilent: false));
    }

    [Fact]
    public void SomeLossAndJitter_IsFair()
    {
        var mos = AgentHealthEndpoints.Mos(rttMs: 180, jitterMs: 30, lossPct: 5);
        Assert.Equal("fair", AgentHealthEndpoints.Grade(mos, false));
    }

    [Fact]
    public void HeavyLoss_IsPoor()
    {
        var mos = AgentHealthEndpoints.Mos(rttMs: 300, jitterMs: 60, lossPct: 12);
        Assert.Equal("poor", AgentHealthEndpoints.Grade(mos, false));
    }

    [Fact]
    public void SilentMic_IsPoor_EvenOnACleanLine()
    {
        Assert.Equal("poor", AgentHealthEndpoints.Grade(4.4, micSilent: true));
    }

    [Fact]
    public void NoStatsYet_IsUnknown()
    {
        Assert.Null(AgentHealthEndpoints.Mos(null, null, null));
        Assert.Equal("unknown", AgentHealthEndpoints.Grade(null, false));
    }

    [Fact]
    public void Store_PushesOnChange_OrEvery15s()
    {
        var store = new AgentHealthStore();
        var t = Guid.NewGuid(); var a = Guid.NewGuid();
        AgentHealth H(string grade, DateTimeOffset at) => new(true, 4.2, grade, 0, 0, 5, 60, false, 0.2, false, false, true, "4g", 10, at);
        var now = DateTimeOffset.UtcNow;
        Assert.True(store.Put(t, a, H("good", now)));                 // first reading
        Assert.False(store.Put(t, a, H("good", now.AddSeconds(5))));   // same grade, 5 s later
        Assert.True(store.Put(t, a, H("poor", now.AddSeconds(10))));   // grade changed
        Assert.False(store.Put(t, a, H("poor", now.AddSeconds(20))));
        Assert.True(store.Put(t, a, H("poor", now.AddSeconds(26))));   // 16 s since the last push
        Assert.NotNull(store.Get(t, a));
    }
}
