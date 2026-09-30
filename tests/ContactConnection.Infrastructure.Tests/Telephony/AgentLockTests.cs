using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Telephony;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>Supervisor lock (S166, Call Records "Finalize"): a locked agent is held Unavailable no
/// matter what asks otherwise, and only an explicit unlock lifts it.</summary>
public class AgentLockTests
{
    private static (AgentStateStore Store, Mock<IDatabase> Redis, Mock<IAgentLockReader> Locks) NewStore()
    {
        var redis = new Mock<IDatabase>();
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redis.Object);
        var locks = new Mock<IAgentLockReader>();
        var store = new AgentStateStore(mux.Object, Mock.Of<IAgentStateHistoryRepository>(), Mock.Of<IDashboardNotifier>(), locks.Object);
        return (store, redis, locks);
    }

    private static string? SavedJson(Mock<IDatabase> redis) =>
        redis.Invocations.Where(i => i.Method.Name == "StringSetAsync").Select(i => i.Arguments[1].ToString()).LastOrDefault();

    [Theory]
    [InlineData(AgentStateCodes.Available)]
    [InlineData(AgentStateCodes.OnCall)]
    [InlineData(AgentStateCodes.Acw)]
    public async Task LockedAgent_IsHeldUnavailable_WhateverIsRequested(string requested)
    {
        var (store, redis, locks) = NewStore();
        var agentId = Guid.NewGuid();
        locks.Setup(l => l.GetAsync("tenant_x", agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentLockInfo(false, "Relieved", "Sue", DateTimeOffset.UtcNow));

        await store.SetAsync(Guid.NewGuid(), agentId, "tenant_x", new AgentStateEntry(requested, requested, null, DateTimeOffset.UtcNow));

        var json = SavedJson(redis)!;
        Assert.Contains("\"code\":\"unavailable\"", json);
        Assert.Contains(AgentStateStore.LockedLabel, json);
    }

    [Fact]
    public async Task LockedAgent_CanStillSignOut_AndUnlockedAgentIsUntouched()
    {
        var (store, redis, locks) = NewStore();
        var locked = Guid.NewGuid();
        locks.Setup(l => l.GetAsync("tenant_x", locked, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentLockInfo(true, null, "Sue", DateTimeOffset.UtcNow));

        await store.SetAsync(Guid.NewGuid(), locked, "tenant_x", new AgentStateEntry(AgentStateCodes.LoggedOut, "Logged Out", null, DateTimeOffset.UtcNow));
        Assert.Contains("\"code\":\"logged_out\"", SavedJson(redis)!);

        await store.SetAsync(Guid.NewGuid(), Guid.NewGuid(), "tenant_x", new AgentStateEntry(AgentStateCodes.Available, "Available", null, DateTimeOffset.UtcNow));
        Assert.Contains("\"code\":\"available\"", SavedJson(redis)!);
    }

    [Fact]
    public void Agent_LockEscalatesToSignIn_NeverRelaxes_AndUnlockClearsEverything()
    {
        var agent = Agent.Create(Guid.NewGuid(), "Pat", "Agent", "pat@example.com", "hash");
        agent.Lock("Sue", "Terminated", signIn: true);
        agent.Lock("Sue", "Again", signIn: false);   // a later status-only lock keeps the sign-in lock
        Assert.True(agent.IsStatusLocked);
        Assert.True(agent.SignInLocked);
        Assert.Equal("Again", agent.StatusLockReason);

        agent.Unlock();
        Assert.False(agent.IsStatusLocked);
        Assert.False(agent.SignInLocked);
        Assert.Null(agent.StatusLockedByName);
    }

    [Fact]
    public void CallRecord_Finalize_MarksComplete_KeepingAnExistingEndTime()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        record.Complete();
        var ended = record.CallEndAt;
        var by = Guid.NewGuid();

        record.Finalize(by, "Sue", "  Agent's script was lost — order taken by phone  ");

        Assert.Equal(CallRecordStatus.Complete, record.OverallStatus);
        Assert.Equal(ended, record.CallEndAt);
        Assert.Equal((by, "Sue", "Agent's script was lost — order taken by phone"), (record.FinalizedById!.Value, record.FinalizedByName, record.FinalizeReason));
        Assert.NotNull(record.FinalizedAt);
    }
}
