using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>Parallel-queuing data model (docs/design/parallel-queuing.md): group routing tiers,
/// per-member campaign exclusions, and the routed tier stamped on the call.</summary>
public class ParallelQueuingTierTests
{
    [Fact]
    public void GroupAssignment_DefaultsToRegularTier()
    {
        var a = GroupCampaignAssignment.Create(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(0, a.RoutingTier);
        Assert.Null(a.ExclusiveWindowSeconds);
        Assert.Null(a.TierLabel);
    }

    [Fact]
    public void SetRouting_StoresTierWindowAndTrimmedLabel()
    {
        var a = GroupCampaignAssignment.Create(Guid.NewGuid(), Guid.NewGuid());
        a.SetRouting(10, 15, "  Alpha ");
        Assert.Equal(10, a.RoutingTier);
        Assert.Equal(15, a.ExclusiveWindowSeconds);
        Assert.Equal("Alpha", a.TierLabel);

        a.SetRouting(10, null, " ");
        Assert.Null(a.ExclusiveWindowSeconds);
        Assert.Null(a.TierLabel);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(101, null)]
    [InlineData(10, 0)]
    [InlineData(10, 601)]
    [InlineData(0, 15)]   // exclusive window only makes sense on a priority tier
    public void SetRouting_RejectsInvalid(int tier, int? window)
    {
        var a = GroupCampaignAssignment.Create(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => a.SetRouting(tier, window, null));
    }

    [Fact]
    public void MemberCampaignExclusion_Create()
    {
        var (g, ag, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var e = AgentGroupMemberCampaignExclusion.Create(g, ag, c);
        Assert.NotEqual(Guid.Empty, e.Id);
        Assert.Equal((g, ag, c), (e.GroupId, e.AgentId, e.CampaignId));
    }

    [Fact]
    public void CallRecord_SetRoutedTier()
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).AddInteraction(InteractionType.CustomerService);
        var group = Guid.NewGuid();
        record.SetRoutedTier(group, 10, "Alpha");
        Assert.Equal(group, record.RoutedGroupId);
        Assert.Equal(10, record.RoutedTier);
        Assert.Equal("Alpha", record.RoutedTierLabel);
    }

    [Fact]
    public void Campaign_ExternalRouting_DefaultsAndValidation()
    {
        var c = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "NeuroQ", "neuroq");
        Assert.Equal(ExternalRoutingAcceptMode.QueueCount, c.ExternalRoutingAcceptMode);
        Assert.Null(c.ExternalRoutingLimit);

        c.SetExternalRouting(ExternalRoutingAcceptMode.QueueWait, 45);
        Assert.Equal((ExternalRoutingAcceptMode.QueueWait, (int?)45), (c.ExternalRoutingAcceptMode, c.ExternalRoutingLimit));

        c.SetExternalRouting(ExternalRoutingAcceptMode.AgentAvailable, 45);   // limit unused → dropped
        Assert.Null(c.ExternalRoutingLimit);

        Assert.Throws<ArgumentException>(() => c.SetExternalRouting("always", null));
        Assert.Throws<ArgumentException>(() => c.SetExternalRouting(ExternalRoutingAcceptMode.QueueCount, 0));
    }

    [Fact]
    public void ExternalRoutingRequest_Factories()
    {
        var r = ExternalRoutingRequest.Routing(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            "sess-1", "8009399174", "5035551234", false, "No Agents Available", []);
        Assert.Equal((ExternalRoutingRequestKind.Routing, (bool?)false, (string?)null), (r.Kind, r.Accepted, r.Targets));

        var ci = ExternalRoutingRequest.CallInfo(Guid.NewGuid(), Guid.NewGuid(), "c-1", "2026-09-27 10:00", "8009399174", null);
        Assert.Equal((ExternalRoutingRequestKind.CallInfo, (bool?)null, "c-1"), (ci.Kind, ci.Accepted, ci.ExternalSessionId));
    }
}
