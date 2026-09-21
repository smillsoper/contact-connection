using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class IvrMenuNodeHandlerTests
{
    private const string Uuid = "call-uuid-1";
    private const string BuiltinPrompt = "__builtin:/usr/share/freeswitch/sounds/menu.wav";

    private static IvrMenuNodeHandler NewHandler(ISttStreamingService? sttStreaming = null, IConfiguration? config = null)
    {
        config ??= new ConfigurationBuilder().AddInMemoryCollection().Build();
        sttStreaming ??= NoSttProvider();
        return new IvrMenuNodeHandler(
            new Mock<ITenantDbContextFactory>().Object, sttStreaming, config, NullLogger<IvrMenuNodeHandler>.Instance);
    }

    /// <summary>No tenant SttStreaming preference configured — the default for every test that
    /// isn't specifically exercising the voice-recognition branch, so the existing digits-only
    /// behavior below is unaffected by S148's addition.</summary>
    private static ISttStreamingService NoSttProvider()
    {
        var stt = new Mock<ISttStreamingService>();
        stt.Setup(s => s.ResolveProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((SttStreamingProviderInfo?)null);
        return stt.Object;
    }

    private static TelephonyFlowContext Ctx(IEslCommander? esl) => new()
    {
        ChannelUuid = Uuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        Esl = esl,
    };

    private static JsonObject MenuNode() => new()
    {
        ["type"] = "tf_ivr_menu",
        ["promptAudioFileId"] = BuiltinPrompt,
        ["maxDigits"] = 1,
        ["maxTries"] = 3,
        ["timeoutMs"] = 5000,
        ["interDigitTimeoutMs"] = 3000,
        ["options"] = new JsonArray
        {
            new JsonObject { ["digit"] = "1", ["transition"] = "sales" },
            new JsonObject { ["digit"] = "2", ["transition"] = "support" },
        },
        ["transitions"] = new JsonObject
        {
            ["sales"] = "node_sales",
            ["support"] = "node_support",
            ["no_match"] = "node_operator",
        },
    };

    private static Mock<IEslCommander> NewEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.AnswerChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.StartAudioStreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        // Default: an Opus-negotiated channel (this codebase's codec-prefs list Opus first) —
        // matches what live-verification (S149) actually found. Override per-test for the
        // narrowband/G711 fallback path.
        esl.Setup(e => e.GetChannelVarAsync(It.IsAny<string>(), "read_codec", It.IsAny<CancellationToken>()))
           .ReturnsAsync("opus");
        return esl;
    }

    [Fact]
    public async Task HappyPath_SetsIvrVars_TransfersToIvrCollect_AndStoresContinuation()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);

        var result = await NewHandler().ExecuteAsync(MenuNode(), ctx);

        Assert.Null(result.NextNodeId);
        Assert.Equal("collecting", result.TransitionTaken);

        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_min", "1", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_max", "1", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_tries", "3", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_term", "none", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_prompt", "/usr/share/freeswitch/sounds/menu.wav", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_invalid", "silence_stream://250", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_regex", "^(1|2)$", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_digit_timeout", "3000", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.TransferAsync(Uuid, "ivr_collect", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("true", ctx.Vars["_ivr_in_progress"]);
        Assert.Equal("node_operator", ctx.Vars["_ivr_no_match"]);
        Assert.Contains("\"1\":\"node_sales\"", ctx.Vars["_ivr_options"]);
        Assert.Contains("\"2\":\"node_support\"", ctx.Vars["_ivr_options"]);
    }

    [Fact]
    public async Task MultiDigit_DefaultsTerminatorToHash()
    {
        var esl = NewEsl();
        var node = MenuNode();
        node["maxDigits"] = 4;
        await NewHandler().ExecuteAsync(node, Ctx(esl.Object));
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_ivr_term", "#", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoEsl_FollowsNoMatchTransition()
    {
        var result = await NewHandler().ExecuteAsync(MenuNode(), Ctx(esl: null));
        Assert.Equal("node_operator", result.NextNodeId);
        Assert.Equal("no_match", result.TransitionTaken);
    }

    [Fact]
    public async Task NoPromptAudio_FollowsNoMatch_AndDoesNotTransfer()
    {
        var esl = NewEsl();
        var node = MenuNode();
        node.Remove("promptAudioFileId");
        var result = await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        Assert.Equal("node_operator", result.NextNodeId);
        esl.Verify(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnwiredOption_IsOmittedFromOptionMap()
    {
        var esl = NewEsl();
        var node = MenuNode();
        ((JsonObject)node["transitions"]!).Remove("support");
        var ctx = Ctx(esl.Object);
        await NewHandler().ExecuteAsync(node, ctx);

        Assert.Contains("\"1\":\"node_sales\"", ctx.Vars["_ivr_options"]);
        Assert.DoesNotContain("\"2\"", ctx.Vars["_ivr_options"]);
    }

    // ── alwaysListen (hot-digit / async) mode — S141 ──────────────────────────────

    private static JsonObject HotDigitNode() => new()
    {
        ["type"] = "tf_ivr_menu",
        ["nodeId"] = "tf_ivr_menu_hot",
        ["alwaysListen"] = true,
        ["options"] = new JsonArray
        {
            new JsonObject { ["digit"] = "1", ["transition"] = "callback" },
        },
        ["transitions"] = new JsonObject
        {
            ["default"] = "node_hold_loop",
            ["callback"] = "node_callback_offer",
        },
    };

    [Fact]
    public async Task AlwaysListen_ArmsListener_ReturnsDefaultImmediately_NoEslCalls()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);

        var result = await NewHandler().ExecuteAsync(HotDigitNode(), ctx);

        Assert.Equal("node_hold_loop", result.NextNodeId);
        Assert.Equal("armed", result.TransitionTaken);
        Assert.Equal("tf_ivr_menu_hot", ctx.Vars["_hot_digit_node_id"]);
        Assert.Contains("\"1\":\"node_callback_offer\"", ctx.Vars["_hot_digit_options"]);

        esl.Verify(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        esl.Verify(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AlwaysListen_NoEslNeeded_WorksEvenWithoutOne()
    {
        // Async mode never touches the channel at all — unlike sync mode, a missing ESL
        // connection shouldn't push it to no_match (there's no capture to fail).
        var ctx = Ctx(esl: null);
        var result = await NewHandler().ExecuteAsync(HotDigitNode(), ctx);
        Assert.Equal("node_hold_loop", result.NextNodeId);
    }

    [Fact]
    public async Task AlwaysListen_MultiDigitOption_IsSkipped_SingleDigitOnly()
    {
        var node = HotDigitNode();
        ((JsonArray)node["options"]!).Add(new JsonObject { ["digit"] = "12", ["transition"] = "callback" });
        var ctx = Ctx(NewEsl().Object);

        await NewHandler().ExecuteAsync(node, ctx);

        Assert.Contains("\"1\":\"node_callback_offer\"", ctx.Vars["_hot_digit_options"]);
        Assert.DoesNotContain("\"12\"", ctx.Vars["_hot_digit_options"]);
    }

    [Fact]
    public async Task AlwaysListen_NoOptionsWired_ArmsEmptyMap_StillReturnsDefault()
    {
        var node = HotDigitNode();
        node["options"] = new JsonArray();
        var ctx = Ctx(NewEsl().Object);

        var result = await NewHandler().ExecuteAsync(node, ctx);

        Assert.Equal("node_hold_loop", result.NextNodeId);
        Assert.Equal("{}", ctx.Vars["_hot_digit_options"]);
    }

    // ── Voice recognition — S148 ──────────────────────────────────────────────────

    private static JsonObject MenuNodeWithPhrases() => new()
    {
        ["type"] = "tf_ivr_menu",
        ["nodeId"] = "node_voice_menu",
        ["promptAudioFileId"] = BuiltinPrompt,
        ["maxDigits"] = 1,
        ["timeoutMs"] = 6000,
        ["options"] = new JsonArray
        {
            new JsonObject
            {
                ["digit"] = "1", ["transition"] = "sales",
                ["phrases"] = new JsonArray { "yes", "yeah" },
            },
            new JsonObject { ["digit"] = "2", ["transition"] = "support" },
        },
        ["transitions"] = new JsonObject
        {
            ["sales"] = "node_sales",
            ["support"] = "node_support",
            ["no_match"] = "node_operator",
        },
    };

    private static Mock<ISttStreamingService> SttProviderConfigured()
    {
        var stt = new Mock<ISttStreamingService>();
        stt.Setup(s => s.ResolveProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new SttStreamingProviderInfo("elevenlabs", null));
        stt.Setup(s => s.PrepareCaptureAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SttStreamingProviderInfo>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync("relay-token-1");
        return stt;
    }

    [Fact]
    public async Task VoiceEnabled_ProviderConfigured_StartsCapture_NoTransfer()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        var stt = SttProviderConfigured();

        var result = await NewHandler(stt.Object).ExecuteAsync(MenuNodeWithPhrases(), ctx);

        Assert.Null(result.NextNodeId);
        Assert.Equal("collecting", result.TransitionTaken);

        esl.Verify(e => e.AnswerChannelAsync(Uuid, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.StartAudioStreamAsync(Uuid, It.IsAny<string>(), "mono", "16k", "relay-token-1", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.BroadcastAsync(Uuid, "/usr/share/freeswitch/sounds/menu.wav", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal("node_voice_menu", ctx.Vars["_ivr_voice_node_id"]);
        Assert.Contains("\"1\":\"node_sales\"", ctx.Vars["_ivr_voice_digit_options"]);
        Assert.Contains("\"2\":\"node_support\"", ctx.Vars["_ivr_voice_digit_options"]);

        stt.Verify(s => s.PrepareCaptureAsync(
            Uuid, "test-tenant", It.IsAny<SttStreamingProviderInfo>(),
            It.Is<IReadOnlyDictionary<string, string>>(m => m["yes"] == "1" && m["yeah"] == "1"),
            It.Is<IReadOnlyDictionary<string, string>>(m => m["1"] == "node_sales" && m["2"] == "node_support"),
            "node_operator", 6000, 48000, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>A plain G711 channel (read_codec="PCMU"/"PCMA", or missing entirely on an older
    /// FreeSWITCH build) must request "8k"/8000 — the historical assumption, still correct for
    /// narrowband. Only wideband channels (Opus, G.722; S149) need a higher declared rate.</summary>
    [Fact]
    public async Task VoiceEnabled_NarrowbandChannel_Requests8kSampleRate()
    {
        var esl = NewEsl();
        esl.Setup(e => e.GetChannelVarAsync(Uuid, "read_codec", It.IsAny<CancellationToken>()))
           .ReturnsAsync("PCMU");
        var ctx = Ctx(esl.Object);
        var stt = SttProviderConfigured();

        await NewHandler(stt.Object).ExecuteAsync(MenuNodeWithPhrases(), ctx);

        esl.Verify(e => e.StartAudioStreamAsync(Uuid, It.IsAny<string>(), "mono", "8k", "relay-token-1", It.IsAny<CancellationToken>()), Times.Once);
        stt.Verify(s => s.PrepareCaptureAsync(
            Uuid, "test-tenant", It.IsAny<SttStreamingProviderInfo>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<int>(), 8000, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>G.722's SDP-declared clock rate is fixed at 8000 for historical compatibility
    /// (RFC 3551 §4.5.2) even though the codec actually carries 16kHz audio — the one case where
    /// the codec name and its "obvious" rate disagree. Easy to get backwards; pinned down
    /// directly.</summary>
    [Fact]
    public async Task VoiceEnabled_G722Channel_Requests16kSampleRate_DespiteSdpSaying8000()
    {
        var esl = NewEsl();
        esl.Setup(e => e.GetChannelVarAsync(Uuid, "read_codec", It.IsAny<CancellationToken>()))
           .ReturnsAsync("G722");
        var ctx = Ctx(esl.Object);
        var stt = SttProviderConfigured();

        await NewHandler(stt.Object).ExecuteAsync(MenuNodeWithPhrases(), ctx);

        esl.Verify(e => e.StartAudioStreamAsync(Uuid, It.IsAny<string>(), "mono", "16k", "relay-token-1", It.IsAny<CancellationToken>()), Times.Once);
        stt.Verify(s => s.PrepareCaptureAsync(
            Uuid, "test-tenant", It.IsAny<SttStreamingProviderInfo>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<string?>(), It.IsAny<int>(), 16000, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VoiceEnabled_NoTenantProvider_FallsBackToDigitsOnly()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);

        var result = await NewHandler(NoSttProvider()).ExecuteAsync(MenuNodeWithPhrases(), ctx);

        Assert.Equal("collecting", result.TransitionTaken);
        esl.Verify(e => e.TransferAsync(Uuid, "ivr_collect", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.StartAudioStreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VoiceEnabled_MultiDigitMenu_FallsBackToDigitsOnly_IgnoresPhrases()
    {
        var esl = NewEsl();
        var node = MenuNodeWithPhrases();
        node["maxDigits"] = 4;
        var ctx = Ctx(esl.Object);
        var stt = SttProviderConfigured();

        await NewHandler(stt.Object).ExecuteAsync(node, ctx);

        esl.Verify(e => e.TransferAsync(Uuid, "ivr_collect", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
        stt.Verify(s => s.ResolveProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
