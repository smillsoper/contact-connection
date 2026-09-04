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

    private static TenantDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private TransferNodeHandler NewHandler(Dictionary<string, string?>? config = null, TenantDbContext? db = null)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(_engine.Object);
        var sp = services.BuildServiceProvider();

        var factory = new Mock<ITenantDbContextFactory>();
        if (db is not null)
            factory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);

        return new TransferNodeHandler(
            factory.Object,
            new EligibleAgentRanker(new Mock<IAgentStateStore>().Object),
            new Mock<ICallStateHistoryRecorder>().Object,
            new Mock<ITelephonyCallSessionStore>().Object,
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
    public async Task Announcement_BuiltinFile_BroadcastToCaller()
    {
        var esl = NewEsl();
        var flowId = Guid.NewGuid();
        _engine.Setup(x => x.SwitchFlowAsync(Uuid, flowId, It.IsAny<IEslCommander>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await NewHandler().ExecuteAsync(
            Node("telephony_flow", new JsonObject { ["targetTelephonyFlowId"] = flowId.ToString(), ["announceAudioFileId"] = "__builtin:/hold.wav" }),
            Ctx(esl.Object));

        esl.Verify(e => e.BroadcastAsync(Uuid, "/hold.wav", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Announcement_TtsFallback_RoutedThroughFliteChannelVar()
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
        esl.Verify(e => e.BroadcastAsync(Uuid, "tts://flite|slt|${cc_xfer_announce_text}", It.IsAny<CancellationToken>()), Times.Once);
    }
}
