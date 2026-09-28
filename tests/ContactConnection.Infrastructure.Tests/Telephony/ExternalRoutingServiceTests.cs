using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>External routing API backing (docs/design/parallel-queuing.md): TMS Dial800Routing's
/// accept rule, number normalization, and availability counting over the queue engine's own data.</summary>
public class ExternalRoutingServiceTests
{
    private static CampaignAvailability Avail(int loggedIn = 1, int available = 0, int queued = 0, int longestWait = 0) =>
        new(loggedIn, available, loggedIn - available, queued, longestWait, []);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void QueueCount_DefaultLimitOne_AcceptsOnlyWhenNobodyQueued(int queued)
    {
        var d = ExternalRoutingService.Decide(ExternalRoutingAcceptMode.QueueCount, null, Avail(queued: queued));
        Assert.Equal(queued == 0, d.Accepted);
    }

    [Fact]
    public void EveryMode_RejectsWhenNobodyLoggedIn()
    {
        foreach (var mode in new[] { ExternalRoutingAcceptMode.QueueCount, ExternalRoutingAcceptMode.QueueWait, ExternalRoutingAcceptMode.AgentAvailable })
        {
            var d = ExternalRoutingService.Decide(mode, 100, Avail(loggedIn: 0));
            Assert.False(d.Accepted);
            Assert.Equal(ExternalRoutingService.NoAgentsAvailable, d.Reason);
        }
    }

    [Fact]
    public void QueueCount_And_QueueWait_And_AgentAvailable()
    {
        Assert.True(ExternalRoutingService.Decide(ExternalRoutingAcceptMode.QueueCount, 3, Avail(queued: 2)).Accepted);
        Assert.False(ExternalRoutingService.Decide(ExternalRoutingAcceptMode.QueueCount, 3, Avail(queued: 3)).Accepted);

        Assert.True(ExternalRoutingService.Decide(ExternalRoutingAcceptMode.QueueWait, 60, Avail(queued: 5, longestWait: 59)).Accepted);
        Assert.False(ExternalRoutingService.Decide(ExternalRoutingAcceptMode.QueueWait, 60, Avail(queued: 5, longestWait: 60)).Accepted);

        Assert.False(ExternalRoutingService.Decide(ExternalRoutingAcceptMode.AgentAvailable, null, Avail(available: 0)).Accepted);
        Assert.True(ExternalRoutingService.Decide(ExternalRoutingAcceptMode.AgentAvailable, null, Avail(loggedIn: 2, available: 1)).Accepted);
    }

    [Theory]
    [InlineData("tel:+18009399174", "8009399174")]
    [InlineData("+1 (800) 939-9174", "8009399174")]
    [InlineData("8009399174", "8009399174")]
    [InlineData("+442071234567", "442071234567")]
    [InlineData(null, "")]
    public void NormalizeNumber(string? raw, string expected) =>
        Assert.Equal(expected, ExternalRoutingService.NormalizeNumber(raw));

    [Fact]
    public async Task Availability_CountsLoggedInAvailablePerTier_AndQueuedByGroupRestriction()
    {
        await using var db = new TenantDbContext(
            new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenantId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var alphaGroup = Guid.NewGuid();
        var eliteGroup = Guid.NewGuid();
        var (regAvail, regBusy, regOut, alphaAvail, eliteBusy) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(regAvail, campaignId));
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(regBusy, campaignId));
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(regOut, campaignId));
        var alpha = GroupCampaignAssignment.Create(alphaGroup, campaignId);
        alpha.SetRouting(10, null, "Alpha");
        db.GroupCampaignAssignments.Add(alpha);
        db.AgentGroupMembers.Add(AgentGroupMember.Create(alphaGroup, alphaAvail));
        var elite = GroupCampaignAssignment.Create(eliteGroup, campaignId);
        elite.SetRouting(0, null, "Elite");
        db.GroupCampaignAssignments.Add(elite);
        db.AgentGroupMembers.Add(AgentGroupMember.Create(eliteGroup, eliteBusy));
        await db.SaveChangesAsync();

        var states = new Mock<IAgentStateStore>();
        AgentStateEntry S(string code) => new(code, code, null, DateTimeOffset.UtcNow);
        states.Setup(s => s.GetAsync(tenantId, regAvail, default)).ReturnsAsync(S(AgentStateCodes.Available));
        states.Setup(s => s.GetAsync(tenantId, regBusy, default)).ReturnsAsync(S(AgentStateCodes.OnCall));
        states.Setup(s => s.GetAsync(tenantId, regOut, default)).ReturnsAsync(S(AgentStateCodes.LoggedOut));
        states.Setup(s => s.GetAsync(tenantId, alphaAvail, default)).ReturnsAsync(S(AgentStateCodes.Available));
        states.Setup(s => s.GetAsync(tenantId, eliteBusy, default)).ReturnsAsync(S(AgentStateCodes.OnCall));

        TelephonyCallSession Queued(int waitedSeconds, Guid? restrict = null)
        {
            var s = new TelephonyCallSession { ChannelUuid = Guid.NewGuid().ToString(), CampaignId = campaignId };
            s.Vars["_queued"] = "true";
            s.Vars["_in_queue_at"] = DateTimeOffset.UtcNow.AddSeconds(-waitedSeconds).ToString("O");
            if (restrict is { } r) s.Vars["_restrict_group_id"] = r.ToString();
            return s;
        }
        var sessions = new Mock<ITelephonyCallSessionStore>();
        sessions.Setup(s => s.GetAllAsync(default)).ReturnsAsync(new List<TelephonyCallSession>
        {
            Queued(40), Queued(10), Queued(90, eliteGroup),
            new() { ChannelUuid = "active", CampaignId = campaignId },               // on a call, not queued
        });

        var service = new ExternalRoutingService(states.Object, sessions.Object);

        var all = await service.GetAvailabilityAsync(db, tenantId, campaignId, null);
        Assert.Equal((4, 2, 2), (all.LoggedIn, all.Available, all.Unavailable));   // logged-out agent not counted
        Assert.Equal(2, all.Queued);                                                 // Elite-pinned call excluded
        Assert.InRange(all.LongestWaitSeconds, 39, 41);
        Assert.Equal([10, 0], all.Tiers.Select(t => t.Tier));
        Assert.Equal(("Alpha", 1, 1), (all.Tiers[0].Label, all.Tiers[0].LoggedIn, all.Tiers[0].Available));

        var eliteOnly = await service.GetAvailabilityAsync(db, tenantId, campaignId, eliteGroup);
        Assert.Equal((1, 0, 1), (eliteOnly.LoggedIn, eliteOnly.Available, eliteOnly.Queued));
        Assert.False(ExternalRoutingService.Decide(ExternalRoutingAcceptMode.QueueCount, null, eliteOnly).Accepted);
    }
}
