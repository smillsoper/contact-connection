using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Telephony;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>Covers EffectivePriorityCalculator — Queue Acceleration's actual formula.</summary>
public class EffectivePriorityCalculatorTests
{
    private static Campaign NewCampaign(
        int priority = 5, bool accelerationEnabled = false, int intervalSeconds = 60, int boost = 1)
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "Test", "test");
        campaign.Update(
            name: "Test", description: null,
            direction: CampaignDirection.Inbound, dialMode: CampaignDialMode.Manual,
            priority: priority, afterCallWorkSeconds: 30, callerIdNumber: null,
            maxQueueSize: 50, queueTimeoutSeconds: 300, serviceLevelThresholdSeconds: 30,
            shortAbandonThresholdSeconds: 10,
            queueAccelerationEnabled: accelerationEnabled,
            queueAccelerationIntervalSeconds: intervalSeconds,
            queueAccelerationPriorityBoost: boost,
            ringStrategy: CampaignRingStrategy.RingAll, ringTopN: 3);
        return campaign;
    }

    [Fact]
    public void AccelerationDisabled_ReturnsFlatPriority_RegardlessOfWaitTime()
    {
        var campaign = NewCampaign(priority: 7, accelerationEnabled: false);
        Assert.Equal(7, EffectivePriorityCalculator.Compute(campaign, secondsWaited: 0));
        Assert.Equal(7, EffectivePriorityCalculator.Compute(campaign, secondsWaited: 10_000));
    }

    [Fact]
    public void AccelerationEnabled_NoTimeElapsed_ReturnsFlatPriority()
    {
        var campaign = NewCampaign(priority: 5, accelerationEnabled: true, intervalSeconds: 60, boost: 2);
        Assert.Equal(5, EffectivePriorityCalculator.Compute(campaign, secondsWaited: 0));
    }

    [Theory]
    [InlineData(59, 5)]   // just under one interval — no boost yet
    [InlineData(60, 7)]   // exactly one interval — one boost applied (floor behavior)
    [InlineData(119, 7)]  // just under two intervals — still only one boost
    [InlineData(120, 9)]  // exactly two intervals — two boosts
    public void AccelerationEnabled_FloorsToCompleteIntervals(double secondsWaited, int expected)
    {
        var campaign = NewCampaign(priority: 5, accelerationEnabled: true, intervalSeconds: 60, boost: 2);
        Assert.Equal(expected, EffectivePriorityCalculator.Compute(campaign, secondsWaited));
    }

    [Fact]
    public void AccelerationEnabled_MultipleIntervalsElapsed_AccumulatesBoost()
    {
        var campaign = NewCampaign(priority: 1, accelerationEnabled: true, intervalSeconds: 30, boost: 3);
        // 300 seconds / 30 = 10 complete intervals * boost 3 = 30, plus base priority 1
        Assert.Equal(31, EffectivePriorityCalculator.Compute(campaign, secondsWaited: 300));
    }
}
