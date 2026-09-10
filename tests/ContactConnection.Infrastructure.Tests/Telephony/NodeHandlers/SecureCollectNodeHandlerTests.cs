using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_secure_collect — PCI guided-DTMF capture. This node kicks off field 0 (sets cc_sc_* vars +
/// uuid_transfer into the secure_collect extension); EslBackgroundService drives the rest. Covers
/// the config guards, the state it stashes, recording masking, and the mid-bridge agent hold.
/// </summary>
public class SecureCollectNodeHandlerTests
{
    private const string Uuid   = "call-uuid-sc";
    private const string Schema = "tenant_test_tenant";
    private static readonly Guid TenantId = Guid.Parse("cccccccc-0000-0000-0000-0000000000aa");

    private static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection().Build();

    private sealed class Harness
    {
        public Mock<IEslCommander> Esl { get; } = new();
        public Mock<ICallRecordingController> Recording { get; } = new();
        public Mock<ISensitiveDataProtector> Protector { get; } = new();
        public Mock<ITelephonyCallSessionStore> SessionStore { get; } = new();
        public TenantDbContext Db { get; }
        public SecureCollectNodeHandler Handler { get; }

        public Harness(bool protectorConfigured = true, CallRecord? record = null)
        {
            Db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            if (record is not null) { Db.CallRecords.Add(record); Db.SaveChanges(); }

            var dbFactory = new Mock<ITenantDbContextFactory>();
            dbFactory.Setup(f => f.Create(It.IsAny<string>())).Returns(Db);

            Protector.SetupGet(p => p.IsConfigured).Returns(protectorConfigured);

            Recording.Setup(r => r.MaskAsync(It.IsAny<RecordingMaskCommand>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RecordingActionOutcome(true));

            SessionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((TelephonyCallSession?)null);

            var eslFactory = new Mock<IEslCommanderFactory>(); // unused — ctx.Esl is supplied

            Handler = new SecureCollectNodeHandler(
                dbFactory.Object, SessionStore.Object, Recording.Object, Protector.Object,
                eslFactory.Object, Config, NullLogger<SecureCollectNodeHandler>.Instance);
        }
    }

    private static TelephonyFlowContext Ctx(IEslCommander esl, params (string k, string v)[] vars)
    {
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = Uuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
            TenantId = TenantId, CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
            TenantSubdomain = "test-tenant", TenantSchemaName = Schema, TenantTimezone = "America/Chicago",
            Esl = esl,
        };
        foreach (var (k, v) in vars) ctx.Vars[k] = v;
        return ctx;
    }

    private static JsonObject Node(JsonArray? fields = null)
    {
        return new JsonObject
        {
            ["type"] = "tf_secure_collect",
            ["nodeId"] = "tf_secure_collect_1",
            ["maxTries"] = 3,
            ["fields"] = fields ?? new JsonArray(
                new JsonObject { ["key"] = "pan", ["minDigits"] = 13, ["maxDigits"] = 19, ["validation"] = "luhn" },
                new JsonObject { ["key"] = "cvv", ["minDigits"] = 3, ["maxDigits"] = 4, ["validation"] = "cvv" }),
            ["transitions"] = new JsonObject
            {
                ["collected"] = "n_done", ["failed"] = "n_fail", ["timeout"] = "n_timeout",
            },
        };
    }

    [Fact]
    public async Task NoFields_TakesFailed_NoEslCalls()
    {
        var h = new Harness();
        var ctx = Ctx(h.Esl.Object);

        var result = await h.Handler.ExecuteAsync(Node(fields: new JsonArray()), ctx);

        Assert.Equal("failed", result.TransitionTaken);
        Assert.Equal("n_fail", result.NextNodeId);
        h.Esl.Verify(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProtectorNotConfigured_TakesFailed()
    {
        var h = new Harness(protectorConfigured: false);
        var result = await h.Handler.ExecuteAsync(Node(), Ctx(h.Esl.Object));

        Assert.Equal("failed", result.TransitionTaken);
        h.Esl.Verify(e => e.TransferAsync(It.IsAny<string>(), "secure_collect", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HappyPath_PreAgent_StashesState_ArmsFirstField_Transfers()
    {
        var h = new Harness();
        var ctx = Ctx(h.Esl.Object);

        var result = await h.Handler.ExecuteAsync(Node(), ctx);

        Assert.Equal("collecting", result.TransitionTaken);
        Assert.Null(result.NextNodeId); // terminal — EslBackgroundService drives from here

        Assert.Equal("true", ctx.Vars["_sc_in_progress"]);
        Assert.Equal("tf_secure_collect_1", ctx.Vars["_sc_node_id"]);
        Assert.Equal("0", ctx.Vars["_sc_field_index"]);
        Assert.Equal("false", ctx.Vars["_sc_rebridge"]);
        Assert.Equal("n_done", ctx.Vars["_sc_next_collected"]);
        Assert.Equal("n_fail", ctx.Vars["_sc_next_failed"]);
        Assert.Equal("n_timeout", ctx.Vars["_sc_next_timeout"]);
        Assert.Contains("\"k\":\"pan\"", ctx.Vars["_sc_fields_json"]);
        Assert.Contains("\"k\":\"cvv\"", ctx.Vars["_sc_fields_json"]);

        // field 0's cc_sc_* vars set, then transfer into the extension
        h.Esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_sc_field", "pan", It.IsAny<CancellationToken>()), Times.Once);
        h.Esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_sc_regex", @"^\d{13,19}$", It.IsAny<CancellationToken>()), Times.Once);
        h.Esl.Verify(e => e.TransferAsync(Uuid, "secure_collect", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
        h.Recording.Verify(r => r.MaskAsync(It.IsAny<RecordingMaskCommand>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Bridged_ParksAgentLegOnHold_MarksRebridge()
    {
        var h = new Harness();
        var ctx = Ctx(h.Esl.Object, ("_bridged_peer_uuid", "agent-leg-uuid"));

        await h.Handler.ExecuteAsync(Node(), ctx);

        Assert.Equal("true", ctx.Vars["_sc_rebridge"]);
        Assert.Equal("agent-leg-uuid", ctx.Vars["_sc_peer_uuid"]);
        h.Esl.Verify(e => e.TransferAsync("agent-leg-uuid", "park_with_moh", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordingActive_MasksForTheCapture()
    {
        var record = CallRecord.CreateInbound(TenantId, "+15551110000", contactIdExternal: Uuid);
        record.AppendRecordingEvent(RecordingEvent.Start(
            DateTimeOffset.UtcNow.AddSeconds(-10), RecordingEventSource.FlowNode, "tf_record_1", "/rec/x.wav"));

        var h = new Harness(record: record);
        var ctx = Ctx(h.Esl.Object);
        // point the ctx at the record we seeded
        var ctxWithRecord = new TelephonyFlowContext
        {
            ChannelUuid = Uuid, CallerNumber = ctx.CallerNumber, DestinationNumber = ctx.DestinationNumber,
            TenantId = TenantId, CampaignId = ctx.CampaignId, CallRecordId = record.Id,
            TenantSubdomain = "test-tenant", TenantSchemaName = Schema, TenantTimezone = "America/Chicago",
            Esl = h.Esl.Object,
        };

        await h.Handler.ExecuteAsync(Node(), ctxWithRecord);

        Assert.Equal("true", ctxWithRecord.Vars["_sc_recording_masked"]);
        h.Recording.Verify(r => r.MaskAsync(
            It.Is<RecordingMaskCommand>(c => c.Source == RecordingEventSource.SecureCollect && c.MaskFill == MaskFillKind.Silence),
            It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
