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
/// Step 3 of the call-recording build: tf_record node — action dispatch to
/// ICallRecordingController and the campaign RecordingMode ceiling.
/// </summary>
public class RecordNodeHandlerTests
{
    private static readonly Guid CampaignId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Schema = "tenant_test_tenant";

    private static TenantDbContext NewDb(Campaign? campaign)
    {
        var db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        if (campaign is not null) { db.Campaigns.Add(campaign); db.SaveChanges(); }
        return db;
    }

    private static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection().Build();

    private static Campaign CampaignWith(string mode, bool stereo = true, string consentModel = "one_party")
    {
        var c = Campaign.Create(Guid.NewGuid(), Guid.NewGuid(), "Rec", "rec");
        typeof(Campaign).GetProperty(nameof(Campaign.Id))!.SetValue(c, CampaignId);
        c.ConfigureRecording(mode, consentModel,
            recordingRequired: false, recordStereo: stereo, recordingBeepEnabled: false,
            autoMaskOnHold: false, recordingRetentionDays: 90);
        return c;
    }

    private static (RecordNodeHandler Handler, Mock<ICallRecordingController> Rec) NewHandler(TenantDbContext db)
    {
        var dbFactory = new Mock<ITenantDbContextFactory>();
        dbFactory.Setup(f => f.Create(It.IsAny<string>())).Returns(db);
        var rec = new Mock<ICallRecordingController>();
        rec.Setup(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecordingActionOutcome(true));
        rec.Setup(r => r.StopAsync(It.IsAny<RecordingCommand>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecordingActionOutcome(true));
        rec.Setup(r => r.MaskAsync(It.IsAny<RecordingMaskCommand>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecordingActionOutcome(true));
        rec.Setup(r => r.UnmaskAsync(It.IsAny<RecordingCommand>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecordingActionOutcome(true));

        var sessionStore = new Mock<ITelephonyCallSessionStore>();
        sessionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TelephonyCallSession?)null);

        return (new RecordNodeHandler(rec.Object, dbFactory.Object, sessionStore.Object, Config,
            NullLogger<RecordNodeHandler>.Instance), rec);
    }

    private static JsonObject Node(string action, params (string k, JsonNode? v)[] extra)
    {
        var n = new JsonObject
        {
            ["type"] = "tf_record",
            ["action"] = action,
            ["transitions"] = new JsonObject { ["default"] = "next_1" },
        };
        foreach (var (k, v) in extra) n[k] = v;
        return n;
    }

    private static TelephonyFlowContext Ctx(IEslCommander? esl = null, Guid? answeringAgent = null) => new()
    {
        ChannelUuid = "uuid-1", CallerNumber = "+15551112222", DestinationNumber = "+15553334444",
        TenantId = Guid.NewGuid(), CampaignId = CampaignId, CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = Schema, TenantTimezone = "America/Chicago",
        Esl = esl, AnsweringAgentId = answeringAgent,
    };

    // ── start + mode ceiling ───────────────────────────────────────────────

    [Fact]
    public async Task Start_ModeFull_CallsStartWithCampaignStereo_AndFollowsDefault()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full, stereo: true)));
        RecordingStartOptions? captured = null;
        rec.Setup(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .Callback<RecordingCommand, RecordingStartOptions, IEslCommander?, CancellationToken>((_, o, _, _) => captured = o)
            .ReturnsAsync(new RecordingActionOutcome(true));

        var result = await handler.ExecuteAsync(Node("start"), Ctx(esl: Mock.Of<IEslCommander>()));

        rec.Verify(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(captured!.Stereo);
        Assert.Equal("next_1", result.NextNodeId);
    }

    [Fact]
    public async Task Start_ModeDisabled_IsNoOp_ButStillFollowsDefault()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Disabled)));

        var result = await handler.ExecuteAsync(Node("start"), Ctx(esl: Mock.Of<IEslCommander>()));

        rec.Verify(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("next_1", result.NextNodeId);
    }

    [Fact]
    public async Task Start_CampaignStereoFalse_PassesStereoFalse()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full, stereo: false)));
        RecordingStartOptions? captured = null;
        rec.Setup(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .Callback<RecordingCommand, RecordingStartOptions, IEslCommander?, CancellationToken>((_, o, _, _) => captured = o)
            .ReturnsAsync(new RecordingActionOutcome(true));

        await handler.ExecuteAsync(Node("start"), Ctx(esl: Mock.Of<IEslCommander>()));

        Assert.False(captured!.Stereo);
    }

    [Fact]
    public async Task Start_WithRecordLimit_PassesLimit()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full)));
        RecordingStartOptions? captured = null;
        rec.Setup(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .Callback<RecordingCommand, RecordingStartOptions, IEslCommander?, CancellationToken>((_, o, _, _) => captured = o)
            .ReturnsAsync(new RecordingActionOutcome(true));

        await handler.ExecuteAsync(Node("start", ("recordLimitSeconds", 1800)), Ctx(esl: Mock.Of<IEslCommander>()));

        Assert.Equal(1800, captured!.LimitSeconds);
    }

    [Fact]
    public async Task Start_ConversationMode_NotBridged_StillProceeds()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Conversation)));

        await handler.ExecuteAsync(Node("start"), Ctx(esl: Mock.Of<IEslCommander>()));

        rec.Verify(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── mask / unmask / stop ───────────────────────────────────────────────

    [Fact]
    public async Task Mask_PassesFillAndMaxMaskSeconds()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full)));
        RecordingMaskCommand? captured = null;
        rec.Setup(r => r.MaskAsync(It.IsAny<RecordingMaskCommand>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .Callback<RecordingMaskCommand, IEslCommander?, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(new RecordingActionOutcome(true));

        await handler.ExecuteAsync(
            Node("mask", ("maskFill", MaskFillKind.Tone), ("maxMaskSeconds", 90), ("reason", "pan")),
            Ctx(esl: Mock.Of<IEslCommander>()));

        Assert.Equal(MaskFillKind.Tone, captured!.MaskFill);
        Assert.Equal(90, captured.MaxMaskSeconds);
        Assert.Equal("pan", captured.Reason);
    }

    [Fact]
    public async Task Unmask_CallsUnmask()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full)));
        await handler.ExecuteAsync(Node("unmask"), Ctx(esl: Mock.Of<IEslCommander>()));
        rec.Verify(r => r.UnmaskAsync(It.IsAny<RecordingCommand>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Stop_CallsStop()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full)));
        await handler.ExecuteAsync(Node("stop"), Ctx(esl: Mock.Of<IEslCommander>()));
        rec.Verify(r => r.StopAsync(It.IsAny<RecordingCommand>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownAction_DoesNothing_FollowsDefault()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full)));

        var result = await handler.ExecuteAsync(Node("frobnicate"), Ctx(esl: Mock.Of<IEslCommander>()));

        rec.VerifyNoOtherCalls();
        Assert.Equal("next_1", result.NextNodeId);
    }

    // ── audit source attribution ───────────────────────────────────────────

    [Fact]
    public async Task EventBranchContext_NoEsl_AttributesSourceToCustomEvent()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full)));
        RecordingMaskCommand? captured = null;
        rec.Setup(r => r.MaskAsync(It.IsAny<RecordingMaskCommand>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .Callback<RecordingMaskCommand, IEslCommander?, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(new RecordingActionOutcome(true));

        await handler.ExecuteAsync(Node("mask"), Ctx(esl: null));

        Assert.Equal(RecordingEventSource.CustomEvent, captured!.Source);
    }

    [Fact]
    public async Task PreAnswerContext_WithEsl_AttributesSourceToFlowNode()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full)));
        RecordingCommand? captured = null;
        rec.Setup(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .Callback<RecordingCommand, RecordingStartOptions, IEslCommander?, CancellationToken>((c, _, _, _) => captured = c)
            .ReturnsAsync(new RecordingActionOutcome(true));

        await handler.ExecuteAsync(Node("start"), Ctx(esl: Mock.Of<IEslCommander>()));

        Assert.Equal(RecordingEventSource.FlowNode, captured!.Source);
    }

    // ── consent-model announcement ─────────────────────────────────────────

    private static Mock<IEslCommander> ConsentEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        esl.Setup(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return esl;
    }

    [Fact]
    public async Task Start_OneParty_NoAnnouncement()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full, consentModel: ConsentModel.OneParty)));
        var esl = ConsentEsl();

        await handler.ExecuteAsync(Node("start"), Ctx(esl: esl.Object));

        esl.Verify(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        rec.Verify(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_TwoPartyAnnounce_WithConsentFile_BroadcastsFileThenStarts()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full, consentModel: ConsentModel.TwoPartyAnnounce)));
        var esl = ConsentEsl();
        var sequence = new List<string>();
        esl.Setup(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("broadcast")).Returns(Task.CompletedTask);
        rec.Setup(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("start")).ReturnsAsync(new RecordingActionOutcome(true));

        await handler.ExecuteAsync(
            Node("start", ("consentAudioFileId", "__builtin:/usr/share/freeswitch/sounds/consent.wav")),
            Ctx(esl: esl.Object));

        esl.Verify(e => e.BroadcastAsync("uuid-1", "/usr/share/freeswitch/sounds/consent.wav", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new[] { "broadcast", "start" }, sequence);
    }

    [Fact]
    public async Task Start_TwoPartyAnnounce_NoConsentFile_FallsBackToTts()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Full, consentModel: ConsentModel.TwoPartyAnnounce)));
        var esl = ConsentEsl();

        await handler.ExecuteAsync(Node("start"), Ctx(esl: esl.Object));

        esl.Verify(e => e.SetChannelVarAsync("uuid-1", "cc_consent_text", It.Is<string>(s => s.Contains("recorded")), It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.BroadcastAsync("uuid-1", "tts://flite|kal|${cc_consent_text}", It.IsAny<CancellationToken>()), Times.Once);
        rec.Verify(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_TwoPartyAnnounceOptout_AlsoAnnounces()
    {
        var (handler, _) = NewHandler(NewDb(CampaignWith(RecordingMode.Full, consentModel: ConsentModel.TwoPartyAnnounceOptout)));
        var esl = ConsentEsl();

        await handler.ExecuteAsync(Node("start"), Ctx(esl: esl.Object));

        esl.Verify(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_TwoPartyAnnounce_ModeDisabled_StillNoOp_NoAnnouncement()
    {
        var (handler, rec) = NewHandler(NewDb(CampaignWith(RecordingMode.Disabled, consentModel: ConsentModel.TwoPartyAnnounce)));
        var esl = ConsentEsl();

        await handler.ExecuteAsync(Node("start"), Ctx(esl: esl.Object));

        esl.Verify(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        rec.Verify(r => r.StartAsync(It.IsAny<RecordingCommand>(), It.IsAny<RecordingStartOptions>(), It.IsAny<IEslCommander?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
