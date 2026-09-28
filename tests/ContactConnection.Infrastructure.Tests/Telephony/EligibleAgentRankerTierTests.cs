using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>Parallel-queuing tiers in EligibleAgentRanker (docs/design/parallel-queuing.md): tier
/// ordering, highest-available-tier offers, exclusive windows, per-member campaign exclusions,
/// "only offer to this group" (Elite), and the delivery-time route stamp.</summary>
public class EligibleAgentRankerTierTests
{
    private static TenantDbContext NewDb() => new(
        new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Mock<IAgentStateStore> AllAvailable()
    {
        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync(new AgentStateEntry(AgentStateCodes.Available, "Available", null, DateTimeOffset.UtcNow));
        return stateStore;
    }

    private static void Busy(Mock<IAgentStateStore> stateStore, Guid agentId) =>
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), agentId, default))
            .ReturnsAsync(new AgentStateEntry(AgentStateCodes.OnCall, "On Call", null, DateTimeOffset.UtcNow));

    private static GroupCampaignAssignment Tiered(Guid groupId, Guid campaignId, int tier, string? label, int? window = null, int proficiency = 50)
    {
        var a = GroupCampaignAssignment.Create(groupId, campaignId, proficiency);
        a.SetRouting(tier, window, label);
        return a;
    }

    [Fact]
    public async Task HigherTierRanksFirst_EvenOverHigherProficiency_AndCarriesLabel()
    {
        await using var db = NewDb();
        var (campaignId, alphaGroup, regular, alpha) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(regular, campaignId, proficiency: 99));
        db.GroupCampaignAssignments.Add(Tiered(alphaGroup, campaignId, 10, "Alpha", proficiency: 10));
        db.AgentGroupMembers.Add(AgentGroupMember.Create(alphaGroup, alpha));
        await db.SaveChangesAsync();

        var ranked = await new EligibleAgentRanker(AllAvailable().Object)
            .GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), campaignId);

        Assert.Equal([alpha, regular], ranked.Select(r => r.AgentId));
        Assert.Equal((10, (Guid?)alphaGroup, "Alpha"), (ranked[0].Tier, ranked[0].GroupId, ranked[0].TierLabel));
        Assert.Equal((0, (Guid?)null, (string?)null), (ranked[1].Tier, ranked[1].GroupId, ranked[1].TierLabel));
    }

    [Fact]
    public async Task OfferSet_OnlyHighestAvailableTier_FallsToRegularWhenAlphaBusy()
    {
        await using var db = NewDb();
        var (campaignId, alphaGroup, regular, alpha) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(regular, campaignId));
        db.GroupCampaignAssignments.Add(Tiered(alphaGroup, campaignId, 10, "Alpha"));
        db.AgentGroupMembers.Add(AgentGroupMember.Create(alphaGroup, alpha));
        await db.SaveChangesAsync();

        var stateStore = AllAvailable();
        var ranker = new EligibleAgentRanker(stateStore.Object);

        var offer = await ranker.GetOfferSetAsync(db, Guid.NewGuid(), campaignId, secondsWaited: 0);
        Assert.Equal([alpha], offer.Agents.Select(r => r.AgentId));
        Assert.Equal(10, offer.OfferTier);

        Busy(stateStore, alpha);
        offer = await ranker.GetOfferSetAsync(db, Guid.NewGuid(), campaignId, secondsWaited: 0);
        Assert.Equal([regular], offer.Agents.Select(r => r.AgentId));
        Assert.Equal(0, offer.OfferTier);
    }

    [Fact]
    public async Task OfferSet_ExclusiveWindow_HoldsForTierUntilElapsed()
    {
        await using var db = NewDb();
        var (campaignId, alphaGroup, regular, alpha) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(regular, campaignId));
        db.GroupCampaignAssignments.Add(Tiered(alphaGroup, campaignId, 10, "Alpha", window: 20));
        db.AgentGroupMembers.Add(AgentGroupMember.Create(alphaGroup, alpha));
        await db.SaveChangesAsync();

        var stateStore = AllAvailable();
        Busy(stateStore, alpha);
        var ranker = new EligibleAgentRanker(stateStore.Object);

        var held = await ranker.GetOfferSetAsync(db, Guid.NewGuid(), campaignId, secondsWaited: 5);
        Assert.Empty(held.Agents);
        Assert.Equal(10, held.HeldForTier);

        var released = await ranker.GetOfferSetAsync(db, Guid.NewGuid(), campaignId, secondsWaited: 20);
        Assert.Equal([regular], released.Agents.Select(r => r.AgentId));
        Assert.Null(released.HeldForTier);
    }

    [Fact]
    public void SelectOffer_WindowDoesNotHoldWhenThatTierIsAvailable()
    {
        var alpha = new RankedAgent(Guid.NewGuid(), 50, DateTimeOffset.UtcNow, 10, Guid.NewGuid(), "Alpha");
        var regular = new RankedAgent(Guid.NewGuid(), 50, DateTimeOffset.UtcNow);
        var offer = EligibleAgentRanker.SelectOffer([alpha, regular], [new TierWindow(10, 30)], secondsWaited: 1);
        Assert.Equal([alpha], offer.Agents);
    }

    [Fact]
    public async Task MemberCampaignExclusion_RemovesGroupRouteOnlyForThatCampaign()
    {
        await using var db = NewDb();
        var (neuroQ, jointFood, alphaGroup, agent) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.GroupCampaignAssignments.Add(Tiered(alphaGroup, neuroQ, 10, "Alpha"));
        db.GroupCampaignAssignments.Add(Tiered(alphaGroup, jointFood, 10, "Alpha"));
        db.AgentGroupMembers.Add(AgentGroupMember.Create(alphaGroup, agent));
        db.AgentGroupMemberCampaignExclusions.Add(AgentGroupMemberCampaignExclusion.Create(alphaGroup, agent, jointFood));
        await db.SaveChangesAsync();

        var ranker = new EligibleAgentRanker(AllAvailable().Object);
        Assert.Single(await ranker.GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), neuroQ));
        Assert.Empty(await ranker.GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), jointFood));
    }

    [Fact]
    public async Task RestrictGroup_OnlyThatGroup_NoFallback_NoWindow()
    {
        await using var db = NewDb();
        var (campaignId, eliteGroup, alphaGroup) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (regular, alpha, elite) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(regular, campaignId));
        db.GroupCampaignAssignments.Add(Tiered(alphaGroup, campaignId, 10, "Alpha", window: 60));
        db.GroupCampaignAssignments.Add(Tiered(eliteGroup, campaignId, 0, "Elite"));
        db.AgentGroupMembers.Add(AgentGroupMember.Create(alphaGroup, alpha));
        db.AgentGroupMembers.Add(AgentGroupMember.Create(eliteGroup, elite));
        await db.SaveChangesAsync();

        var stateStore = AllAvailable();
        var ranker = new EligibleAgentRanker(stateStore.Object);

        var offer = await ranker.GetOfferSetAsync(db, Guid.NewGuid(), campaignId, 0, restrictGroupId: eliteGroup);
        var only = Assert.Single(offer.Agents);
        Assert.Equal((elite, "Elite"), (only.AgentId, only.TierLabel));
        Assert.Null(offer.HeldForTier);

        // Elite agent busy → nobody, even though regular and Alpha agents are free.
        Busy(stateStore, elite);
        Assert.Empty((await ranker.GetOfferSetAsync(db, Guid.NewGuid(), campaignId, 0, restrictGroupId: eliteGroup)).Agents);
    }

    [Fact]
    public async Task ResolveRoute_PicksHighestTier_AndRespectsRestriction()
    {
        await using var db = NewDb();
        var (campaignId, alphaGroup, eliteGroup, agent) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(agent, campaignId, proficiency: 90));
        db.GroupCampaignAssignments.Add(Tiered(alphaGroup, campaignId, 10, "Alpha", proficiency: 40));
        db.AgentGroupMembers.Add(AgentGroupMember.Create(alphaGroup, agent));
        db.GroupCampaignAssignments.Add(Tiered(eliteGroup, campaignId, 0, "Elite"));
        await db.SaveChangesAsync();

        var route = await EligibleAgentRanker.ResolveRouteAsync(db, campaignId, agent);
        Assert.Equal((10, (Guid?)alphaGroup, "Alpha"), (route!.Tier, route.GroupId, route.TierLabel));

        // Not an Elite member → no route when restricted to Elite.
        Assert.Null(await EligibleAgentRanker.ResolveRouteAsync(db, campaignId, agent, eliteGroup));
        Assert.Null(await EligibleAgentRanker.ResolveRouteAsync(db, campaignId, Guid.NewGuid()));
    }

    [Fact]
    public void QueueOffer_LabelsAndIdsRoundTrip()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var raw = QueueOffer.FormatLabels([
            new RankedAgent(a, 50, DateTimeOffset.UtcNow, 10, Guid.NewGuid(), "Alpha"),
            new RankedAgent(b, 50, DateTimeOffset.UtcNow)]);
        var labels = QueueOffer.ParseLabels(raw);
        Assert.Equal("Alpha", labels[a]);
        Assert.False(labels.ContainsKey(b));
        Assert.Equal(new HashSet<Guid> { a, b }, QueueOffer.ParseAgentIds($"{a}, {b},junk"));
        Assert.Null(QueueOffer.RestrictGroupId(new Dictionary<string, string>()));
    }
}
