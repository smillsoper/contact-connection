using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_check_agent_availability — branches "available"/"unavailable" via the SAME
/// EligibleAgentRanker query the real queue loop (RouteToQueueNodeHandler / QueuePollingService)
/// uses, rather than its own independent assignment-only check. Fixed S138: the prior "Phase 1"
/// implementation only looked at AgentCampaignAssignment/GroupCampaignAssignment existence and
/// never checked live agent presence at all — an agent row existing said nothing about whether
/// anyone was actually online. These tests cover the delegation, not the ranker's own
/// eligibility rules (see EligibleAgentRankerTests for those).
/// </summary>
public class CheckAgentAvailabilityNodeHandlerTests
{
    private static TenantDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AgentStateEntry Available(DateTimeOffset since) =>
        new(AgentStateCodes.Available, "Available", null, since);

    private static TelephonyFlowContext Ctx(Guid tenantId, Guid campaignId) => new()
    {
        ChannelUuid = "call-uuid-caa", CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
        TenantId = tenantId, CampaignId = campaignId, CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
    };

    private static JsonObject Node(string? campaignIdOverride = null) => new()
    {
        ["type"] = "tf_check_agent_availability",
        ["nodeId"] = "tf_caa_1",
        ["campaignId"] = campaignIdOverride,
        ["transitions"] = new JsonObject { ["available"] = "n_avail", ["unavailable"] = "n_unavail" },
    };

    [Fact]
    public async Task AgentAssignedButNotAvailable_TakesUnavailable_NotJustAssignmentExistence()
    {
        // The old "Phase 1" logic would have reported "available" here — an assignment exists —
        // even though the only assigned agent is on a call, not Available. This is the exact
        // regression the fix targets.
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(agentId, campaignId));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(tenantId, agentId, default))
            .ReturnsAsync(new AgentStateEntry(AgentStateCodes.OnCall, "On Call", null, DateTimeOffset.UtcNow));

        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);
        var handler = new CheckAgentAvailabilityNodeHandler(factory.Object, new EligibleAgentRanker(stateStore.Object));

        var result = await handler.ExecuteAsync(Node(), Ctx(tenantId, campaignId));

        Assert.Equal("unavailable", result.TransitionTaken);
        Assert.Equal("n_unavail", result.NextNodeId);
    }

    [Fact]
    public async Task AgentAssignedAndAvailable_TakesAvailable()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(agentId, campaignId));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(tenantId, agentId, default)).ReturnsAsync(Available(DateTimeOffset.UtcNow));

        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);
        var handler = new CheckAgentAvailabilityNodeHandler(factory.Object, new EligibleAgentRanker(stateStore.Object));

        var result = await handler.ExecuteAsync(Node(), Ctx(tenantId, campaignId));

        Assert.Equal("available", result.TransitionTaken);
        Assert.Equal("n_avail", result.NextNodeId);
    }

    [Fact]
    public async Task NoAssignmentsAtAll_TakesUnavailable()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();

        var stateStore = new Mock<IAgentStateStore>();
        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);
        var handler = new CheckAgentAvailabilityNodeHandler(factory.Object, new EligibleAgentRanker(stateStore.Object));

        var result = await handler.ExecuteAsync(Node(), Ctx(tenantId, campaignId));

        Assert.Equal("unavailable", result.TransitionTaken);
    }

    [Fact]
    public async Task CampaignIdOverride_ChecksTheOverrideCampaign_NotCtxCampaign()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var ctxCampaignId = Guid.NewGuid();
        var overrideCampaignId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // Agent is assigned+available on the OVERRIDE campaign only — ctx.CampaignId has nobody.
        db.AgentCampaignAssignments.Add(AgentCampaignAssignment.Create(agentId, overrideCampaignId));
        await db.SaveChangesAsync();

        var stateStore = new Mock<IAgentStateStore>();
        stateStore.Setup(s => s.GetAsync(tenantId, agentId, default)).ReturnsAsync(Available(DateTimeOffset.UtcNow));

        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);
        var handler = new CheckAgentAvailabilityNodeHandler(factory.Object, new EligibleAgentRanker(stateStore.Object));

        var result = await handler.ExecuteAsync(Node(overrideCampaignId.ToString()), Ctx(tenantId, ctxCampaignId));

        Assert.Equal("available", result.TransitionTaken);
    }
}
