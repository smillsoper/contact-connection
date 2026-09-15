using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>
/// Covers Campaign.Update()'s MaxQueueSize clamp. 0 is a deliberate, meaningful value here (not
/// just "clamped to the floor") -- RouteToQueueNodeHandler and TransferNodeHandler both gate their
/// queue-full check on `MaxQueueSize > 0`, treating 0 as "no ceiling, queue forever". Before this
/// fix, Update() clamped the floor to 1 (`Math.Max(1, ...)`), so 0 could never actually be
/// persisted and that "unlimited" branch in both consumers was unreachable through normal use.
/// </summary>
public class CampaignQueueSizeTests
{
    private static Campaign NewCampaign() =>
        Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "Test Campaign", "test-campaign");

    private static void Update(Campaign campaign, int maxQueueSize) =>
        campaign.Update(
            name: "Test Campaign", description: null,
            direction: CampaignDirection.Inbound, dialMode: CampaignDialMode.Manual,
            priority: 5, afterCallWorkSeconds: 30, callerIdNumber: null,
            maxQueueSize: maxQueueSize, queueTimeoutSeconds: 300, serviceLevelThresholdSeconds: 30,
            shortAbandonThresholdSeconds: 10,
            queueAccelerationEnabled: false, queueAccelerationIntervalSeconds: 60, queueAccelerationPriorityBoost: 1,
            ringStrategy: CampaignRingStrategy.RingAll, ringTopN: 3);

    [Theory]
    [InlineData(0, 0)]    // 0 means unlimited -- must pass through, not clamp up to 1
    [InlineData(-5, 0)]   // negative has no meaning; floors to 0 (unlimited), not 1
    [InlineData(50, 50)]  // valid value passes through unchanged
    public void Update_MaxQueueSize_ClampsToAtLeastZero(int input, int expected)
    {
        var campaign = NewCampaign();
        Update(campaign, input);
        Assert.Equal(expected, campaign.MaxQueueSize);
    }
}
