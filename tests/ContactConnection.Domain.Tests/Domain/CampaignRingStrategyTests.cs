using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>Covers Campaign.Update()'s validation of the new RingStrategy/RingTopN fields —
/// mirrors the existing clamp-guard style already used for every other field on this method
/// (e.g. Priority via Math.Clamp, MaxQueueSize via Math.Max).</summary>
public class CampaignRingStrategyTests
{
    private static Campaign NewCampaign() =>
        Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "Test Campaign", "test-campaign");

    private static void Update(Campaign campaign, string ringStrategy, int ringTopN) =>
        campaign.Update(
            name: "Test Campaign", description: null,
            direction: CampaignDirection.Inbound, dialMode: CampaignDialMode.Manual,
            priority: 5, afterCallWorkSeconds: 30, callerIdNumber: null,
            maxQueueSize: 50, queueTimeoutSeconds: 300, serviceLevelThresholdSeconds: 30,
            shortAbandonThresholdSeconds: 10,
            queueAccelerationEnabled: false, queueAccelerationIntervalSeconds: 60, queueAccelerationPriorityBoost: 1,
            ringStrategy: ringStrategy, ringTopN: ringTopN);

    [Fact]
    public void Create_DefaultsToRingAll()
    {
        var campaign = NewCampaign();
        Assert.Equal(CampaignRingStrategy.RingAll, campaign.RingStrategy);
        Assert.Equal(3, campaign.RingTopN);
    }

    [Theory]
    [InlineData(CampaignRingStrategy.RingAll)]
    [InlineData(CampaignRingStrategy.AutoAnswerBestAgent)]
    [InlineData(CampaignRingStrategy.RingTopNByProficiency)]
    public void Update_ValidRingStrategy_IsAccepted(string ringStrategy)
    {
        var campaign = NewCampaign();
        Update(campaign, ringStrategy, 5);
        Assert.Equal(ringStrategy, campaign.RingStrategy);
    }

    [Fact]
    public void Update_InvalidRingStrategy_FallsBackToRingAll()
    {
        var campaign = NewCampaign();
        Update(campaign, "not_a_real_strategy", 5);
        Assert.Equal(CampaignRingStrategy.RingAll, campaign.RingStrategy);
    }

    [Theory]
    [InlineData(0, 1)]  // clamps up to the floor
    [InlineData(-5, 1)]
    [InlineData(7, 7)]  // valid value passes through unchanged
    public void Update_RingTopN_ClampsToAtLeastOne(int input, int expected)
    {
        var campaign = NewCampaign();
        Update(campaign, CampaignRingStrategy.RingAll, input);
        Assert.Equal(expected, campaign.RingTopN);
    }
}
