using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class TransferNodeHandlerTests
{
    private const string Uuid = "call-uuid-xfer";
    private static readonly Guid CallId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000aa");

    private readonly Mock<ITelephonyFlowEngine> _engine = new();
    private readonly Mock<ITelephonyCallSessionStore> _sessionStore = new();

    private static TenantDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private TransferNodeHandler NewHandler(Dictionary<string, string?>? config = null, TenantDbContext? db = null,
        IEslCommanderFactory? eslFactory = null)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(_engine.Object);
        if (eslFactory is not null) services.AddSingleton(eslFactory);
        var sp = services.BuildServiceProvider();

        var factory = new Mock<ITenantDbContextFactory>();
        if (db is not null)
            factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);

        _sessionStore.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync((IReadOnlyList<TelephonyCallSession>)[]);

        return new TransferNodeHandler(
            factory.Object,
            new EligibleAgentRanker(new Mock<IAgentStateStore>().Object),
            new Mock<ICallStateHistoryRecorder>().Object,
            _sessionStore.Object,
            new Mock<ITtsStreamingService>().Object,
            new Mock<ITtsFileSynthesizer>().Object,
            sp, cfg, NullLogger<TransferNodeHandler>.Instance);
    }

    private static Agent SeedAgent(TenantDbContext db, string ext)
    {
        var a = Agent.Create(TenantId, "Test", "Agent", $"{ext}@x.com", "hash");
        a.SetSipCredentials(ext, "a1");
        db.Agents.Add(a);
        db.SaveChanges();
        return a;
    }

    private static TelephonyFlowContext Ctx(IEslCommander? esl) => new()
    {
        ChannelUuid = Uuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = CallId,
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        Esl = esl,
    };

    private static JsonObject Node(string destType, JsonObject? overrides = null)
    {
        var n = new JsonObject
        {
            ["type"] = "tf_transfer",
            ["nodeId"] = "tf_transfer_1",
            ["destinationType"] = destType,
            ["transitions"] = new JsonObject
            {
                ["transferred"] = "node_ok",
                ["failed"] = "node_fail",
            },
        };
        if (overrides is not null)
            foreach (var kv in overrides) n[kv.Key] = kv.Value?.DeepClone();
        return n;
    }

    private static Mock<IEslCommander> NewEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.BridgeToAgentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        return esl;
    }

    [Fact]
    public async Task NoEsl_Fails()
    {
        var result = await NewHandler().ExecuteAsync(Node("agent"), Ctx(esl: null));
        Assert.Equal("failed", result.TransitionTaken);
        Assert.Equal("node_fail", result.NextNodeId);
    }

    [Fact]
    public async Task Agent_KnownExtension_QueuesToThatAgent()
    {
        await using var db = NewDb();
        var agent = SeedAgent(db, "1042");
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);

        var result = await NewHandler(db: db).ExecuteAsync(
            Node("agent", new JsonObject { ["agentExtension"] = "1042" }), ctx);

        Assert.Equal("transferred", result.TransitionTaken);
        Assert.Equal("true", ctx.Vars["_queued"]);
        Assert.Equal(agent.Id.ToString(), ctx.Vars["_eligible_agents"]);
        Assert.True(ctx.Vars.ContainsKey("_in_queue_at"));
        // No inline bridge — delivery happens on the queue tick.
        esl.Verify(e => e.BridgeToAgentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Agent_UnknownExtension_Fails()
    {
        await using var db = NewDb();
        var esl = NewEsl();
        var result = await NewHandler(db: db).ExecuteAsync(
            Node("agent", new JsonObject { ["agentExtension"] = "9999" }), Ctx(esl.Object));
        Assert.Equal("failed", result.TransitionTaken);
    }

    [Fact]
    public async Task Agent_NoExtension_Fails()
    {
        var esl = NewEsl();
        var result = await NewHandler().ExecuteAsync(Node("agent"), Ctx(esl.Object));
        Assert.Equal("failed", result.TransitionTaken);
    }

    [Fact]
    public async Task External_SipUri_DialsSofiaExternal_AndStashesFailedTarget()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        var result = await NewHandler().ExecuteAsync(
            Node("external_number", new JsonObject { ["externalNumber"] = "sip:help@pbx.client.com" }), ctx);

        Assert.Null(result.NextNodeId);
        Assert.Equal("transferring", result.TransitionTaken);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_xfer_dest", "sofia/external/sip:help@pbx.client.com", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.TransferAsync(Uuid, "xfer_bridge", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("true", ctx.Vars["_xfer_in_progress"]);
        Assert.Equal("tf_transfer_1", ctx.Vars["_xfer_node_id"]);
        Assert.Equal("node_fail", ctx.Vars["_xfer_next_failed"]);
    }

    [Fact]
    public async Task External_BareNumber_UsesNamedGateway()
    {
        var esl = NewEsl();
        await NewHandler().ExecuteAsync(
            Node("external_number", new JsonObject { ["externalNumber"] = "+1 (800) 555-1234", ["externalGatewayName"] = "acme" }),
            Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_xfer_dest", "sofia/gateway/acme/+18005551234", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task External_BareNumber_NoGateway_UsesConfiguredDefault()
    {
        var esl = NewEsl();
        var handler = NewHandler(new Dictionary<string, string?> { ["FreeSWITCH:DefaultGateway"] = "trunk1" });
        await handler.ExecuteAsync(
            Node("external_number", new JsonObject { ["externalNumber"] = "8005551234" }), Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_xfer_dest", "sofia/gateway/trunk1/8005551234", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task External_NoNumber_Fails()
    {
        var esl = NewEsl();
        var result = await NewHandler().ExecuteAsync(Node("external_number"), Ctx(esl.Object));
        Assert.Equal("failed", result.TransitionTaken);
        esl.Verify(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TelephonyFlow_InvalidId_Fails()
    {
        var esl = NewEsl();
        var result = await NewHandler().ExecuteAsync(Node("telephony_flow"), Ctx(esl.Object));
        Assert.Equal("failed", result.TransitionTaken);
    }

    [Fact]
    public async Task TelephonyFlow_SwitchOk_Transferred()
    {
        var esl = NewEsl();
        var flowId = Guid.NewGuid();
        _engine.Setup(x => x.SwitchFlowAsync(Uuid, flowId, It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var result = await NewHandler().ExecuteAsync(
            Node("telephony_flow", new JsonObject { ["targetTelephonyFlowId"] = flowId.ToString() }), Ctx(esl.Object));

        Assert.Equal("transferred", result.TransitionTaken);
        _engine.Verify(x => x.SwitchFlowAsync(Uuid, flowId, It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TelephonyFlow_SwitchReturnsFalse_Fails()
    {
        var esl = NewEsl();
        var flowId = Guid.NewGuid();
        _engine.Setup(x => x.SwitchFlowAsync(Uuid, flowId, It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

        var result = await NewHandler().ExecuteAsync(
            Node("telephony_flow", new JsonObject { ["targetTelephonyFlowId"] = flowId.ToString() }), Ctx(esl.Object));

        Assert.Equal("failed", result.TransitionTaken);
    }

    [Fact]
    public async Task CampaignQueue_InvalidTarget_Fails()
    {
        var esl = NewEsl();
        var result = await NewHandler().ExecuteAsync(Node("campaign_queue"), Ctx(esl.Object));
        Assert.Equal("failed", result.TransitionTaken);
    }

    [Fact]
    public async Task CampaignQueue_Success_MovesCallRecordOntoTargetCampaignAndItsClient()
    {
        // The handler opens (and disposes) its own db context internally, so seed and later
        // re-read through separate contexts sharing one InMemory database name.
        var dbName = Guid.NewGuid().ToString();
        DbContextOptions<TenantDbContext> Options() =>
            new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(dbName).Options;

        var clientId = Guid.NewGuid();
        var targetCampaign = Campaign.Create(TenantId, clientId, "Target Campaign", "target-campaign");
        // Record starts stranded on Guid.Empty client/a different campaign — the exact state a
        // record created via CreateInbound/CreateOutbound is left in before routing resolves it.
        var record = CallRecord.Create(TenantId, Guid.Empty, Guid.NewGuid());
        await using (var seedDb = new TenantDbContext(Options()))
        {
            seedDb.Campaigns.Add(targetCampaign);
            seedDb.CallRecords.Add(record);
            await seedDb.SaveChangesAsync();
        }

        var esl = NewEsl();
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = Uuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
            TenantId = TenantId, CampaignId = Guid.NewGuid(), CallRecordId = record.Id,
            TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
            Esl = esl.Object,
        };
        var result = await NewHandler(db: new TenantDbContext(Options())).ExecuteAsync(
            Node("campaign_queue", new JsonObject { ["targetCampaignId"] = targetCampaign.Id.ToString() }), ctx);

        Assert.Equal("transferred", result.TransitionTaken);
        await using var verifyDb = new TenantDbContext(Options());
        var updated = await verifyDb.CallRecords.FindAsync(record.Id);
        Assert.Equal(targetCampaign.Id, updated!.CampaignId);
        Assert.Equal(clientId, updated.ClientId);
    }

    [Fact]
    public async Task ScreenPopOverride_StashedInFlowVars()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        var spFlow = Guid.NewGuid();

        // Any destination — the override is stashed in ExecuteAsync before the switch.
        await NewHandler().ExecuteAsync(
            Node("external_number", new JsonObject { ["externalNumber"] = "sip:x@y", ["screenPopFlowId"] = spFlow.ToString() }), ctx);

        Assert.Equal(spFlow.ToString(), ctx.Vars["_screenpop_flow_override"]);
    }

    [Fact]
    public async Task Announcement_BuiltinFile_DefersHandoffIntoTtsPlay()
    {
        var esl = NewEsl();
        var flowId = Guid.NewGuid();
        _engine.Setup(x => x.SwitchFlowAsync(Uuid, flowId, It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var ctx = Ctx(esl.Object);

        var result = await NewHandler().ExecuteAsync(
            Node("telephony_flow", new JsonObject { ["targetTelephonyFlowId"] = flowId.ToString(), ["announceAudioFileId"] = "__builtin:/hold.wav" }),
            ctx);

        // First pass: announcement fired into tts_play, node returns terminal, handoff deferred.
        Assert.Equal("transferring", result.TransitionTaken);
        Assert.Null(result.NextNodeId);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_tts_url", "/hold.wav", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.TransferAsync(Uuid, "tts_play", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("true", ctx.Vars["_announce_in_progress"]);
        Assert.Equal("tf_transfer_1", ctx.Vars["_announce_replay_node"]);
        _engine.Verify(x => x.SwitchFlowAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Announcement_TtsFallback_RoutedThroughFliteChannelVarIntoTtsPlay()
    {
        var esl = NewEsl();
        var flowId = Guid.NewGuid();
        _engine.Setup(x => x.SwitchFlowAsync(Uuid, flowId, It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await NewHandler().ExecuteAsync(
            Node("telephony_flow", new JsonObject
            {
                ["targetTelephonyFlowId"] = flowId.ToString(),
                ["announceTtsText"] = "Please hold while we transfer you.",
                ["announceTtsVoice"] = "slt",
            }),
            Ctx(esl.Object));

        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_xfer_announce_text", "Please hold while we transfer you.", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_tts_url", "tts://flite|slt|${cc_xfer_announce_text}", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.TransferAsync(Uuid, "tts_play", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Announcement_SecondPass_SkipsAnnouncement_AndProceedsWithHandoff()
    {
        var esl = NewEsl();
        var flowId = Guid.NewGuid();
        _engine.Setup(x => x.SwitchFlowAsync(Uuid, flowId, It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var ctx = Ctx(esl.Object);
        // HandleTtsDoneAsync sets this before re-running the node.
        ctx.Vars["_announce_done"] = "true";
        ctx.Vars["_announce_replay_node"] = "tf_transfer_1";

        var result = await NewHandler().ExecuteAsync(
            Node("telephony_flow", new JsonObject { ["targetTelephonyFlowId"] = flowId.ToString(), ["announceAudioFileId"] = "__builtin:/hold.wav" }),
            ctx);

        Assert.Equal("transferred", result.TransitionTaken);
        _engine.Verify(x => x.SwitchFlowAsync(Uuid, flowId, It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.TransferAsync(Uuid, "tts_play", "XML", "default", It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(ctx.VarsToRemove.Contains("_announce_done"));
        Assert.True(ctx.VarsToRemove.Contains("_announce_replay_node"));
    }

    // ── Mid-call transfer (S178): sales agent's script fires a CS-queue transfer ──────────────────

    private const string AgentLeg = "agent-leg-uuid";
    private static readonly Guid SalesAgent = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000ee");

    private async Task<(TelephonyNodeResult Result, TelephonyFlowContext Ctx, CallRecord Seeded, Campaign Target,
        DbContextOptions<TenantDbContext> Options, TelephonyCallSession Live)> RunMidCallTransferAsync(
        Mock<IEslCommander>? ctxEsl, IEslCommanderFactory? factory = null)
    {
        var dbName = Guid.NewGuid().ToString();
        DbContextOptions<TenantDbContext> Options() =>
            new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(dbName).Options;

        var salesCampaignId = Guid.NewGuid();
        var csFlow = Guid.NewGuid();
        var target = Campaign.Create(TenantId, Guid.NewGuid(), "NeuroQ CS", "neuroq-cs");
        target.AssignFlow(csFlow);
        var record = CallRecord.Create(TenantId, Guid.NewGuid(), salesCampaignId);
        await using (var seedDb = new TenantDbContext(Options()))
        {
            seedDb.Campaigns.Add(target);
            seedDb.CallRecords.Add(record);
            await seedDb.SaveChangesAsync();
        }

        var live = new TelephonyCallSession { ChannelUuid = Uuid, CallRecordId = record.Id, CampaignId = salesCampaignId };
        _sessionStore.Setup(s => s.GetAsync(Uuid, It.IsAny<CancellationToken>())).ReturnsAsync(live);

        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = Uuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
            TenantId = TenantId, CampaignId = salesCampaignId, CallRecordId = record.Id,
            TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
            Esl = ctxEsl?.Object,
        };
        ctx.Vars["_assigned_agent_id"] = SalesAgent.ToString();
        ctx.Vars["_bridged_peer_uuid"] = AgentLeg;

        var result = await NewHandler(db: new TenantDbContext(Options()), eslFactory: factory).ExecuteAsync(
            Node("campaign_queue", new JsonObject { ["targetCampaignId"] = target.Id.ToString() }), ctx);
        return (result, ctx, record, target, Options(), live);
    }

    [Fact]
    public async Task MidCall_PutsCallerOnHold_DropsAgentLeg_GuardsSavedBeforeTheBridgeIsTouched()
    {
        var esl = NewEsl();
        esl.Setup(e => e.HangupChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var guardsSavedFirst = false;
        _sessionStore.Setup(s => s.SaveAsync(It.IsAny<TelephonyCallSession>(), It.IsAny<CancellationToken>()))
                     .Callback<TelephonyCallSession, CancellationToken>((s, _) =>
                         guardsSavedFirst = s.Vars.GetValueOrDefault("_requeue_in_progress") == "true")
                     .Returns(Task.CompletedTask);
        esl.Setup(e => e.TransferAsync(Uuid, "park_with_moh", "XML", "default", It.IsAny<CancellationToken>()))
           .Callback(() => Assert.True(guardsSavedFirst, "guards must be saved before the caller is moved"))
           .Returns(Task.CompletedTask);

        var (result, ctx, _, _, _, live) = await RunMidCallTransferAsync(esl);

        Assert.Equal("transferred", result.TransitionTaken);
        esl.Verify(e => e.SetChannelVarAsync(AgentLeg, "park_after_bridge", "false", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.TransferAsync(Uuid, "park_with_moh", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.HangupChannelAsync(AgentLeg, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AgentLeg, live.Vars["_requeue_old_leg"]);
        Assert.Equal(SalesAgent.ToString(), live.Vars["_requeued_from_agent_id"]);
        Assert.Equal("true", live.Vars["_keep_record_agent"]);
        // The sales agent is off the call; the next agent's delivery sets these again.
        Assert.Contains("_assigned_agent_id", ctx.VarsToRemove);
        Assert.Equal("true", ctx.Vars["_queued"]);
    }

    [Fact]
    public async Task MidCall_KeepsTheCallRecordOnItsOriginalCampaign_AndPopsTheTargetCampaignsScript()
    {
        var esl = NewEsl();
        esl.Setup(e => e.HangupChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var (_, ctx, seeded, target, options, _) = await RunMidCallTransferAsync(esl);

        await using var verifyDb = new TenantDbContext(options);
        var after = await verifyDb.CallRecords.FindAsync(seeded.Id);
        Assert.Equal(seeded.CampaignId, after!.CampaignId);                       // sales attribution stays
        Assert.Equal(target.Id.ToString(), ctx.Vars["_switch_campaign_id"]);       // routing moves to CS
        Assert.Equal(target.FlowId.ToString(), ctx.Vars["_screenpop_flow_override"]);
    }

    [Fact]
    public async Task MidCall_FromAnEventBranchWithNoEsl_OpensItsOwnConnection()
    {
        var owned = new Mock<IOwnedEslCommander>();
        owned.Setup(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        owned.Setup(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        owned.Setup(e => e.HangupChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        owned.Setup(e => e.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var factory = new Mock<IEslCommanderFactory>();
        factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(owned.Object);

        var (result, _, _, _, _, _) = await RunMidCallTransferAsync(ctxEsl: null, factory.Object);

        Assert.Equal("transferred", result.TransitionTaken);
        owned.Verify(e => e.TransferAsync(Uuid, "park_with_moh", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
        owned.Verify(e => e.DisposeAsync(), Times.Once);
    }
}
