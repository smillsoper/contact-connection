using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

public class DataCollectNodeHandlerTests
{
    private const string Uuid = "call-uuid-1";
    private const string BuiltinPrompt = "__builtin:/usr/share/freeswitch/sounds/prompt.wav";

    private static DataCollectNodeHandler NewHandler(ISttStreamingService? sttStreaming = null, IConfiguration? config = null)
    {
        config ??= new ConfigurationBuilder().AddInMemoryCollection().Build();
        sttStreaming ??= NoSttProvider();
        return new DataCollectNodeHandler(
            new Mock<ITenantDbContextFactory>().Object, sttStreaming, config, NullLogger<DataCollectNodeHandler>.Instance);
    }

    private static ISttStreamingService NoSttProvider()
    {
        var stt = new Mock<ISttStreamingService>();
        stt.Setup(s => s.ResolveProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((SttStreamingProviderInfo?)null);
        return stt.Object;
    }

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

    private static TelephonyFlowContext Ctx(IEslCommander? esl) => new()
    {
        ChannelUuid = Uuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        Esl = esl,
    };

    private static JsonObject Node() => new()
    {
        ["type"] = "tf_data_collect",
        ["nodeId"] = "node_collect_phone",
        ["variableName"] = "entered_phone",
        ["promptAudioFileId"] = BuiltinPrompt,
        ["minDigits"] = 1,
        ["maxDigits"] = 10,
        ["maxTries"] = 3,
        ["timeoutMs"] = 5000,
        ["interDigitTimeoutMs"] = 3000,
        ["transitions"] = new JsonObject
        {
            ["collected"] = "node_confirm",
            ["timeout"] = "node_goodbye",
        },
    };

    private static Mock<IEslCommander> NewEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.StartAudioStreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.GetChannelVarAsync(It.IsAny<string>(), "read_codec", It.IsAny<CancellationToken>()))
           .ReturnsAsync("opus");
        return esl;
    }

    [Fact]
    public async Task HappyPath_SetsDataCollectVars_TransfersToDataCollect_AndStoresContinuation()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);

        var result = await NewHandler().ExecuteAsync(Node(), ctx);

        Assert.Null(result.NextNodeId);
        Assert.Equal("collecting", result.TransitionTaken);

        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_min", "1", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_max", "10", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_tries", "3", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_timeout", "5000", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_term", "#", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_prompt", "/usr/share/freeswitch/sounds/prompt.wav", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_invalid", "silence_stream://250", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_regex", @"\d+", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_digit_timeout", "3000", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.TransferAsync(Uuid, "data_collect", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("true", ctx.Vars["_dc_in_progress"]);
        Assert.Equal("node_collect_phone", ctx.Vars["_dc_node_id"]);
        Assert.Equal("entered_phone", ctx.Vars["_dc_variable_name"]);
        Assert.Equal("node_confirm", ctx.Vars["_dc_next_node"]);
        Assert.Equal("node_goodbye", ctx.Vars["_dc_timeout_node"]);
        Assert.Equal("false", ctx.Vars["_dc_numeric_only"]);
    }

    [Fact]
    public async Task SingleDigit_DefaultsTerminatorToNone()
    {
        var esl = NewEsl();
        var node = Node();
        node["maxDigits"] = 1;
        await NewHandler().ExecuteAsync(node, Ctx(esl.Object));
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_term", "none", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExplicitTerminators_OverrideDefault_RegardlessOfMaxDigits()
    {
        var esl = NewEsl();
        var node = Node();
        node["maxDigits"] = 1;
        node["terminators"] = "*";
        await NewHandler().ExecuteAsync(node, Ctx(esl.Object));
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_dc_term", "*", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NumericOnly_FlagIsPassedThroughAsSessionVar()
    {
        var esl = NewEsl();
        var node = Node();
        node["numericOnly"] = true;
        var ctx = Ctx(esl.Object);
        await NewHandler().ExecuteAsync(node, ctx);
        Assert.Equal("true", ctx.Vars["_dc_numeric_only"]);
    }

    [Fact]
    public async Task NoVariableName_FollowsTimeout_NoEslCalls()
    {
        var esl = NewEsl();
        var node = Node();
        node.Remove("variableName");

        var result = await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        Assert.Equal("node_goodbye", result.NextNodeId);
        Assert.Equal("timeout", result.TransitionTaken);
        esl.Verify(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        esl.Verify(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BlankVariableName_TreatedSameAsMissing()
    {
        var esl = NewEsl();
        var node = Node();
        node["variableName"] = "   ";

        var result = await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        Assert.Equal("node_goodbye", result.NextNodeId);
        Assert.Equal("timeout", result.TransitionTaken);
    }

    [Fact]
    public async Task NoEsl_FollowsTimeoutTransition()
    {
        var result = await NewHandler().ExecuteAsync(Node(), Ctx(esl: null));
        Assert.Equal("node_goodbye", result.NextNodeId);
        Assert.Equal("timeout", result.TransitionTaken);
    }

    [Fact]
    public async Task NoPromptAudio_FollowsTimeout_AndDoesNotTransfer()
    {
        var esl = NewEsl();
        var node = Node();
        node.Remove("promptAudioFileId");

        var result = await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        Assert.Equal("node_goodbye", result.NextNodeId);
        Assert.Equal("timeout", result.TransitionTaken);
        esl.Verify(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingTimeoutTransition_FallsBackToDefault()
    {
        var esl = NewEsl();
        var node = Node();
        node.Remove("promptAudioFileId");
        ((JsonObject)node["transitions"]!).Remove("timeout");
        ((JsonObject)node["transitions"]!)["default"] = "node_fallback";

        var result = await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        Assert.Equal("node_fallback", result.NextNodeId);
    }

    // ── allowVoice — races DTMF against a concurrent STT capture ──────────────────

    [Fact]
    public async Task AllowVoice_NoTenantProvider_FallsBackToDtmfOnly_NoAudioStream()
    {
        var esl = NewEsl();
        var node = Node();
        node["allowVoice"] = true;
        var ctx = Ctx(esl.Object);

        var result = await NewHandler(NoSttProvider()).ExecuteAsync(node, ctx);

        Assert.Equal("collecting", result.TransitionTaken);
        esl.Verify(e => e.TransferAsync(Uuid, "data_collect", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.StartAudioStreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(ctx.Vars.ContainsKey("_dc_voice_active"));
    }

    [Fact]
    public async Task AllowVoice_ProviderConfigured_TransfersFirst_ThenStartsAudioStream()
    {
        // Ordering matters here: starting the media bug before the uuid_transfer was a real,
        // live-found bug (S150) that corrupted the STT relay handshake. Pin the sequence down so
        // a future refactor can't silently reintroduce it.
        var esl = NewEsl();
        var callOrder = new List<string>();
        esl.Setup(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Callback(() => callOrder.Add("transfer"))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.StartAudioStreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Callback(() => callOrder.Add("audio_stream"))
           .Returns(Task.CompletedTask);

        var node = Node();
        node["allowVoice"] = true;
        var ctx = Ctx(esl.Object);
        var stt = SttProviderConfigured();

        var result = await NewHandler(stt.Object).ExecuteAsync(node, ctx);

        Assert.Equal("collecting", result.TransitionTaken);
        Assert.Equal(new[] { "transfer", "audio_stream" }, callOrder);
        esl.Verify(e => e.StartAudioStreamAsync(Uuid, It.IsAny<string>(), "mono", "16k", "relay-token-1", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("true", ctx.Vars["_dc_voice_active"]);

        stt.Verify(s => s.PrepareCaptureAsync(
            Uuid, "test-tenant", It.IsAny<SttStreamingProviderInfo>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
            null, 5000, 48000, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AllowVoice_NarrowbandChannel_Requests8kSampleRate()
    {
        var esl = NewEsl();
        esl.Setup(e => e.GetChannelVarAsync(Uuid, "read_codec", It.IsAny<CancellationToken>()))
           .ReturnsAsync("PCMU");
        var node = Node();
        node["allowVoice"] = true;
        var ctx = Ctx(esl.Object);
        var stt = SttProviderConfigured();

        await NewHandler(stt.Object).ExecuteAsync(node, ctx);

        esl.Verify(e => e.StartAudioStreamAsync(Uuid, It.IsAny<string>(), "mono", "8k", "relay-token-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AllowVoice_G722Channel_Requests16kSampleRate_DespiteSdpSaying8000()
    {
        var esl = NewEsl();
        esl.Setup(e => e.GetChannelVarAsync(Uuid, "read_codec", It.IsAny<CancellationToken>()))
           .ReturnsAsync("G722");
        var node = Node();
        node["allowVoice"] = true;
        var ctx = Ctx(esl.Object);
        var stt = SttProviderConfigured();

        await NewHandler(stt.Object).ExecuteAsync(node, ctx);

        esl.Verify(e => e.StartAudioStreamAsync(Uuid, It.IsAny<string>(), "mono", "16k", "relay-token-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AllowVoiceFalse_NeverConsultsSttProvider()
    {
        var esl = NewEsl();
        var node = Node();
        node["allowVoice"] = false;
        var ctx = Ctx(esl.Object);
        var stt = SttProviderConfigured();

        await NewHandler(stt.Object).ExecuteAsync(node, ctx);

        stt.Verify(s => s.ResolveProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
