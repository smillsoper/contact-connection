using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>
/// Covers EligibleAgentRanker — the shared eligible-agent-building/ranking logic extracted from
/// RouteToQueueNodeHandler and QueuePollingService (previously duplicated, and both missing the
/// GroupCampaignAssignment.IsActive filter — see the dedicated regression test below). Uses a
/// real EF Core InMemory TenantDbContext (same pattern as TenantCredentialAuditServiceTests) plus
/// a mocked IAgentStateStore, since the ranker's Redis lookups are per-agent and easy to stub.
/// </summary>
public class EligibleAgentRankerTests
{
    private static TenantDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TenantDbContext(options);
    }

    private static AgentStateEntry Available(DateTimeOffset since) =>
        new(AgentStateCodes.Available, "Available", null, since);

    private static AgentStateEntry NotAvailable(string code) =>
        new(code, code, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task RanksByProficiencyDescending()
    {
        await using var db = NewDb();
        var campaignId = Guid.NewGuid();
        var lowAgent = Guid.NewGuid();
        var highAgent = Guid.NewGuid();

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(lowAgent, campaignId, proficiency: 20));
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(highAgent, campaignId, proficiency: 90));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        var now = DateTimeOffset.UtcNow;
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), lowAgent, default)).ReturnsAsync(Available(now));
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), highAgent, default)).ReturnsAsync(Available(now));

        var ranker = new EligibleAgentRanker(stateStore.Object);
        var ranked = await ranker.GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), campaignId);

        Assert.Equal([highAgent, lowAgent], ranked.Select(r => r.AgentId));
    }

    [Fact]
    public async Task EqualProficiency_TieBrokenByLongestIdle()
    {
        await using var db = NewDb();
        var campaignId = Guid.NewGuid();
        var recentlyAvailable = Guid.NewGuid();
        var longIdle = Guid.NewGuid();

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(recentlyAvailable, campaignId, proficiency: 50));
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(longIdle, campaignId, proficiency: 50));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), recentlyAvailable, default))
            .ReturnsAsync(Available(DateTimeOffset.UtcNow));
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), longIdle, default))
            .ReturnsAsync(Available(DateTimeOffset.UtcNow.AddMinutes(-30)));

        var ranker = new EligibleAgentRanker(stateStore.Object);
        var ranked = await ranker.GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), campaignId);

        // Same proficiency — the agent available longer (earlier SetAt) ranks first.
        Assert.Equal([longIdle, recentlyAvailable], ranked.Select(r => r.AgentId));
    }

    [Fact]
    public async Task AgentNotAvailable_Excluded()
    {
        await using var db = NewDb();
        var campaignId = Guid.NewGuid();
        var onCallAgent = Guid.NewGuid();
        var availableAgent = Guid.NewGuid();

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(onCallAgent, campaignId));
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(availableAgent, campaignId));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), onCallAgent, default)).ReturnsAsync(NotAvailable(AgentStateCodes.OnCall));
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), availableAgent, default)).ReturnsAsync(Available(DateTimeOffset.UtcNow));

        var ranker = new EligibleAgentRanker(stateStore.Object);
        var ranked = await ranker.GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), campaignId);

        Assert.Equal([availableAgent], ranked.Select(r => r.AgentId));
    }

    [Fact]
    public async Task AgentWithNoStateAtAll_Excluded()
    {
        await using var db = NewDb();
        var campaignId = Guid.NewGuid();
        var neverLoggedIn = Guid.NewGuid();
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(neverLoggedIn, campaignId));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), neverLoggedIn, default)).ReturnsAsync((AgentStateEntry?)null);

        var ranker = new EligibleAgentRanker(stateStore.Object);
        var ranked = await ranker.GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), campaignId);

        Assert.Empty(ranked);
    }

    [Fact]
    public async Task InactiveDirectAssignment_Excluded()
    {
        await using var db = NewDb();
        var campaignId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var assignment = AgentCampaignAssignment.Create(agentId, campaignId);
        assignment.Deactivate();
        db.AgentCampaignAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), agentId, default)).ReturnsAsync(Available(DateTimeOffset.UtcNow));

        var ranker = new EligibleAgentRanker(stateStore.Object);
        var ranked = await ranker.GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), campaignId);

        Assert.Empty(ranked);
    }

    /// <summary>
    /// Regression test for the bug found while building this class: the original inline
    /// eligible-agent queries in both RouteToQueueNodeHandler and QueuePollingService filtered
    /// AgentCampaignAssignment.IsActive but never GroupCampaignAssignment.IsActive — a
    /// deactivated group assignment still contributed ringable agents.
    /// </summary>
    [Fact]
    public async Task InactiveGroupAssignment_Excluded()
    {
        await using var db = NewDb();
        var campaignId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        var groupAssignment = GroupCampaignAssignment.Create(groupId, campaignId, proficiency: 80);
        groupAssignment.Deactivate();
        db.GroupCampaignAssignments.Add(groupAssignment);
        db.AgentGroupMembers.Add(AgentGroupMember.Create(groupId, agentId));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), agentId, default)).ReturnsAsync(Available(DateTimeOffset.UtcNow));

        var ranker = new EligibleAgentRanker(stateStore.Object);
        var ranked = await ranker.GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), campaignId);

        Assert.Empty(ranked);
    }

    [Fact]
    public async Task ActiveGroupAssignment_MembersIncluded_WithGroupProficiency()
    {
        await using var db = NewDb();
        var campaignId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        db.GroupCampaignAssignments.Add(GroupCampaignAssignment.Create(groupId, campaignId, proficiency: 75));
        db.AgentGroupMembers.Add(AgentGroupMember.Create(groupId, agentId));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), agentId, default)).ReturnsAsync(Available(DateTimeOffset.UtcNow));

        var ranker = new EligibleAgentRanker(stateStore.Object);
        var ranked = await ranker.GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), campaignId);

        var only = Assert.Single(ranked);
        Assert.Equal(agentId, only.AgentId);
        Assert.Equal(75, only.EffectiveProficiency);
    }

    [Theory]
    [InlineData(30, 90, 90)] // group proficiency higher — MAX wins
    [InlineData(90, 30, 90)] // direct proficiency higher — MAX wins
    public async Task AgentBothDirectAndGroupAssigned_UsesMaxProficiency(int directProficiency, int groupProficiency, int expectedEffective)
    {
        await using var db = NewDb();
        var campaignId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(agentId, campaignId, directProficiency));
        db.GroupCampaignAssignments.Add(GroupCampaignAssignment.Create(groupId, campaignId, groupProficiency));
        db.AgentGroupMembers.Add(AgentGroupMember.Create(groupId, agentId));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), agentId, default)).ReturnsAsync(Available(DateTimeOffset.UtcNow));

        var ranker = new EligibleAgentRanker(stateStore.Object);
        var ranked = await ranker.GetRankedEligibleAgentsAsync(db, Guid.NewGuid(), campaignId);

        Assert.Equal(expectedEffective, Assert.Single(ranked).EffectiveProficiency);
    }

    [Fact]
    public async Task ExcludeAgentIds_FiltersOutExcludedAgents()
    {
        await using var db = NewDb();
        var campaignId = Guid.NewGuid();
        var excluded = Guid.NewGuid();
        var included = Guid.NewGuid();

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(excluded, campaignId));
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(included, campaignId));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default)).ReturnsAsync(Available(DateTimeOffset.UtcNow));

        var ranker = new EligibleAgentRanker(stateStore.Object);
        var ranked = await ranker.GetRankedEligibleAgentsAsync(
            db, Guid.NewGuid(), campaignId, excludeAgentIds: new HashSet<Guid> { excluded });

        Assert.Equal([included], ranked.Select(r => r.AgentId));
    }
}
