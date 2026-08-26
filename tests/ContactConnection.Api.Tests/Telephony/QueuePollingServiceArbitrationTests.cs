using ContactConnection.Api.Telephony;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Api.Tests.Telephony;

/// <summary>Covers QueuePollingService.OrderByArbitrationPriority — the AutoAnswerBestAgent
/// cross-call arbitration order (highest effective priority first, ties broken by longest
/// wait). Part of the campaign ring-strategy delivery work (Session 92).</summary>
public class QueuePollingServiceArbitrationTests
{
    private static Campaign NewCampaign(int priority, bool accelerationEnabled = false, int intervalSeconds = 60, int boost = 1)
    {
        var campaign = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "Test", $"test-{Guid.NewGuid():N}");
        campaign.Update(
            name: "Test", description: null,
            direction: CampaignDirection.Inbound, dialMode: CampaignDialMode.Manual,
            priority: priority, afterCallWorkSeconds: 30, callerIdNumber: null,
            maxQueueSize: 50, queueTimeoutSeconds: 300, serviceLevelThresholdSeconds: 30,
            shortAbandonThresholdSeconds: 10,
            queueAccelerationEnabled: accelerationEnabled,
            queueAccelerationIntervalSeconds: intervalSeconds,
            queueAccelerationPriorityBoost: boost,
            ringStrategy: CampaignRingStrategy.AutoAnswerBestAgent, ringTopN: 3);
        return campaign;
    }

    private static TelephonyCallSession NewSession() => new() { ChannelUuid = Guid.NewGuid().ToString() };

    [Fact]
    public void HigherEffectivePriority_OrderedFirst()
    {
        var now = DateTimeOffset.UtcNow;
        var lowPriority = (Session: NewSession(), Campaign: NewCampaign(priority: 2), InQueueAt: now);
        var highPriority = (Session: NewSession(), Campaign: NewCampaign(priority: 9), InQueueAt: now);

        var ordered = QueuePollingService.OrderByArbitrationPriority([lowPriority, highPriority], now);

        Assert.Equal([highPriority.Session, lowPriority.Session], ordered.Select(o => o.Session));
    }

    [Fact]
    public void EqualPriority_TieBrokenByLongestWait()
    {
        var now = DateTimeOffset.UtcNow;
        var recentlyQueued = (Session: NewSession(), Campaign: NewCampaign(priority: 5), InQueueAt: now.AddSeconds(-5));
        var longWaiting = (Session: NewSession(), Campaign: NewCampaign(priority: 5), InQueueAt: now.AddSeconds(-120));

        var ordered = QueuePollingService.OrderByArbitrationPriority([recentlyQueued, longWaiting], now);

        Assert.Equal([longWaiting.Session, recentlyQueued.Session], ordered.Select(o => o.Session));
    }

    [Fact]
    public void AccelerationBoost_CanOvertakeAHigherBasePriorityCall()
    {
        var now = DateTimeOffset.UtcNow;
        // Base priority 8, just arrived — no boost accrued yet.
        var freshHighPriority = (
            Session: NewSession(),
            Campaign: NewCampaign(priority: 8, accelerationEnabled: true, intervalSeconds: 30, boost: 3),
            InQueueAt: now);
        // Base priority 3, but has been waiting 5 whole intervals (150s / 30s) * boost 3 = +15 -> effective 18.
        var longWaitingLowPriority = (
            Session: NewSession(),
            Campaign: NewCampaign(priority: 3, accelerationEnabled: true, intervalSeconds: 30, boost: 3),
            InQueueAt: now.AddSeconds(-150));

        var ordered = QueuePollingService.OrderByArbitrationPriority([freshHighPriority, longWaitingLowPriority], now);

        // The long-waiting call's accelerated priority (18) now outranks the fresh call's flat 8 —
        // this is Queue Acceleration's whole point: don't starve a lower-priority campaign forever.
        Assert.Equal([longWaitingLowPriority.Session, freshHighPriority.Session], ordered.Select(o => o.Session));
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var ordered = QueuePollingService.OrderByArbitrationPriority([], DateTimeOffset.UtcNow);
        Assert.Empty(ordered);
    }
}
