using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_script_pop's flow-resolution fallback chain (node override → transfer screen-pop override →
/// DID-level PhoneNumber.FlowId → campaign fallback) had zero coverage. Also covers the
/// FlowEngine-failure swallow (a broken CRM flow must not take the telephony call down with it)
/// and the ctx.FlowEngine/AnsweringAgentId/InteractionId "no live agent context" guard.
/// </summary>
public class ScriptPopNodeHandlerTests
{
    private static TenantDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ScriptPopNodeHandler NewHandler(TenantDbContext db, ITenantRepository? tenantRepo = null)
    {
        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);
        tenantRepo ??= Mock.Of<ITenantRepository>();
        return new ScriptPopNodeHandler(factory.Object, tenantRepo, new TenantContext(), NullLogger<ScriptPopNodeHandler>.Instance);
    }

    private static TelephonyFlowContext Ctx(
        Guid campaignId, Guid tenantId, IFlowEngine? flowEngine, string destinationNumber = "+15552220000") => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551110000", DestinationNumber = destinationNumber,
        TenantId = tenantId, CampaignId = campaignId, CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        FlowEngine = flowEngine, AnsweringAgentId = Guid.NewGuid(), InteractionId = Guid.NewGuid(),
    };

    private static JsonObject Node(string? flowId = null) => new()
    {
        ["type"] = "tf_script_pop",
        ["flowId"] = flowId,
        ["transitions"] = new JsonObject { ["default"] = "node_next" },
    };

    private static Mock<IFlowEngine> NewFlowEngine(Guid sessionId)
    {
        var engine = new Mock<IFlowEngine>();
        engine.Setup(e => e.StartAsync(It.IsAny<StartFlowRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new FlowNodeState
              {
                  SessionId = sessionId, CallRecordId = Guid.NewGuid(), NodeId = "n1", NodeType = "script", Label = "Greeting",
              });
        return engine;
    }

    [Fact]
    public async Task MissingAgentContext_TakesNoContext_DoesNotCallFlowEngine()
    {
        await using var db = NewDb();
        var engine = NewFlowEngine(Guid.NewGuid());
        var ctx = Ctx(Guid.NewGuid(), Guid.NewGuid(), engine.Object);
        ctx.AnsweringAgentId = null;

        var result = await NewHandler(db).ExecuteAsync(Node(), ctx);

        Assert.Equal("no_context", result.TransitionTaken);
        Assert.Equal("node_next", result.NextNodeId);
        engine.Verify(e => e.StartAsync(It.IsAny<StartFlowRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoFlowEngine_TakesNoContext()
    {
        await using var db = NewDb();
        var ctx = Ctx(Guid.NewGuid(), Guid.NewGuid(), flowEngine: null);

        var result = await NewHandler(db).ExecuteAsync(Node(), ctx);

        Assert.Equal("no_context", result.TransitionTaken);
    }

    [Fact]
    public async Task NodeLevelFlowIdOverride_UsedDirectly_HighestPriority()
    {
        await using var db = NewDb();
        var flowId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        // Different flow on the campaign — proves the node override wins, not just "any flow found".
        var campaignFlowId = Guid.NewGuid();
        var campaign = Campaign.Create(tenantId, Guid.NewGuid(), "Camp", "camp");
        campaign.AssignFlow(campaignFlowId);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        var engine = NewFlowEngine(Guid.NewGuid());
        var ctx = Ctx(campaign.Id, tenantId, engine.Object);

        await NewHandler(db).ExecuteAsync(Node(flowId.ToString()), ctx);

        engine.Verify(e => e.StartAsync(It.Is<StartFlowRequest>(r => r.FlowId == flowId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScreenPopOverrideVar_UsedWhenNoNodeOverride()
    {
        await using var db = NewDb();
        var overrideFlowId = Guid.NewGuid();
        var engine = NewFlowEngine(Guid.NewGuid());
        var ctx = Ctx(Guid.NewGuid(), Guid.NewGuid(), engine.Object);
        ctx.Vars["_screenpop_flow_override"] = overrideFlowId.ToString();

        await NewHandler(db).ExecuteAsync(Node(), ctx);

        engine.Verify(e => e.StartAsync(It.Is<StartFlowRequest>(r => r.FlowId == overrideFlowId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NodeFlowId_TakesPriorityOver_ScreenPopOverrideVar()
    {
        await using var db = NewDb();
        var nodeFlowId = Guid.NewGuid();
        var overrideFlowId = Guid.NewGuid();
        var engine = NewFlowEngine(Guid.NewGuid());
        var ctx = Ctx(Guid.NewGuid(), Guid.NewGuid(), engine.Object);
        ctx.Vars["_screenpop_flow_override"] = overrideFlowId.ToString();

        await NewHandler(db).ExecuteAsync(Node(nodeFlowId.ToString()), ctx);

        engine.Verify(e => e.StartAsync(It.Is<StartFlowRequest>(r => r.FlowId == nodeFlowId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DidLevelFlowOverride_UsedWhenNoHigherPriorityOverrideSet()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var didFlowId = Guid.NewGuid();
        var phone = PhoneNumber.Create(tenantId, campaignId, "+15552220000");
        phone.AssignFlow(didFlowId);
        db.PhoneNumbers.Add(phone);
        db.Campaigns.Add(Campaign.Create(tenantId, Guid.NewGuid(), "Camp", "camp"));
        await db.SaveChangesAsync();

        var engine = NewFlowEngine(Guid.NewGuid());
        var ctx = Ctx(campaignId, tenantId, engine.Object, destinationNumber: "+15552220000");

        await NewHandler(db).ExecuteAsync(Node(), ctx);

        engine.Verify(e => e.StartAsync(It.Is<StartFlowRequest>(r => r.FlowId == didFlowId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InactiveDid_IsIgnored_FallsBackToCampaign()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var didFlowId = Guid.NewGuid();
        var campaignFlowId = Guid.NewGuid();

        var phone = PhoneNumber.Create(tenantId, campaignId, "+15552220000");
        phone.AssignFlow(didFlowId);
        phone.Deactivate();
        db.PhoneNumbers.Add(phone);

        var campaign = Campaign.Create(tenantId, Guid.NewGuid(), "Camp", "camp");
        campaign.AssignFlow(campaignFlowId);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        var engine = NewFlowEngine(Guid.NewGuid());
        var ctx = Ctx(campaign.Id, tenantId, engine.Object, destinationNumber: "+15552220000");

        await NewHandler(db).ExecuteAsync(Node(), ctx);

        engine.Verify(e => e.StartAsync(It.Is<StartFlowRequest>(r => r.FlowId == campaignFlowId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoDidMatch_FallsBackToCampaignFlow()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var campaignFlowId = Guid.NewGuid();
        var campaign = Campaign.Create(tenantId, Guid.NewGuid(), "Camp", "camp");
        campaign.AssignFlow(campaignFlowId);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        var engine = NewFlowEngine(Guid.NewGuid());
        var ctx = Ctx(campaign.Id, tenantId, engine.Object, destinationNumber: "+19998887777");

        await NewHandler(db).ExecuteAsync(Node(), ctx);

        engine.Verify(e => e.StartAsync(It.Is<StartFlowRequest>(r => r.FlowId == campaignFlowId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoFlowResolvedAnywhere_TakesNoFlow_DoesNotCallFlowEngine()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var campaign = Campaign.Create(tenantId, Guid.NewGuid(), "Camp", "camp"); // no FlowId assigned
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();

        var engine = NewFlowEngine(Guid.NewGuid());
        var ctx = Ctx(campaign.Id, tenantId, engine.Object, destinationNumber: "+19998887777");

        var result = await NewHandler(db).ExecuteAsync(Node(), ctx);

        Assert.Equal("no_flow", result.TransitionTaken);
        Assert.Equal("node_next", result.NextNodeId);
        engine.Verify(e => e.StartAsync(It.IsAny<StartFlowRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulStart_SerializesSessionStateIntoCrmSessionVar()
    {
        await using var db = NewDb();
        var sessionId = Guid.NewGuid();
        var engine = NewFlowEngine(sessionId);
        var ctx = Ctx(Guid.NewGuid(), Guid.NewGuid(), engine.Object);

        var result = await NewHandler(db).ExecuteAsync(Node(Guid.NewGuid().ToString()), ctx);

        Assert.Equal("default", result.TransitionTaken);
        Assert.Equal("node_next", result.NextNodeId);
        Assert.True(ctx.Vars.ContainsKey("_crm_session_json"));
        using var doc = JsonDocument.Parse(ctx.Vars["_crm_session_json"]);
        Assert.Equal(sessionId, doc.RootElement.GetProperty("sessionId").GetGuid());
    }

    [Fact]
    public async Task FlowEngineThrows_IsSwallowed_StillFollowsDefaultTransition()
    {
        await using var db = NewDb();
        var engine = new Mock<IFlowEngine>();
        engine.Setup(e => e.StartAsync(It.IsAny<StartFlowRequest>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("flow is broken"));
        var ctx = Ctx(Guid.NewGuid(), Guid.NewGuid(), engine.Object);

        var result = await NewHandler(db).ExecuteAsync(Node(Guid.NewGuid().ToString()), ctx);

        Assert.Equal("default", result.TransitionTaken);
        Assert.Equal("node_next", result.NextNodeId);
        Assert.False(ctx.Vars.ContainsKey("_crm_session_json"));
    }

    [Fact]
    public async Task TenantContextCurrent_IsPopulated_WhenUnset()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var tenant = Tenant.Create("Test Tenant", "test-tenant", "America/Chicago");
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var factory = new Mock<ITenantDbContextFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);
        var tenantContext = new TenantContext();
        var handler = new ScriptPopNodeHandler(factory.Object, tenantRepo.Object, tenantContext, NullLogger<ScriptPopNodeHandler>.Instance);

        var engine = NewFlowEngine(Guid.NewGuid());
        var ctx = Ctx(Guid.NewGuid(), tenantId, engine.Object);

        await handler.ExecuteAsync(Node(Guid.NewGuid().ToString()), ctx);

        Assert.Same(tenant, tenantContext.Current);
        tenantRepo.Verify(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
