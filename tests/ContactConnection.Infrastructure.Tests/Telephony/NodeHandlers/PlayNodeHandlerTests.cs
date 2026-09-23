using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_play had zero coverage despite branching across two entirely different media sources (file
/// vs. TTS) and, within TTS, two entirely different playback/resume mechanisms (flite
/// uuid_broadcast vs. a streaming vendor's foreground tts_play transfer) — exactly the kind of
/// surface where a real bug (S150: TTS text never resolved {{variable}} tags because ttsText was
/// passed straight to the provider with zero interpolation) went unnoticed for a long time. This
/// file locks down both paths and that specific regression.
/// </summary>
public class PlayNodeHandlerTests
{
    private const string Uuid = "call-uuid-1";
    private const string BuiltinFile = "__builtin:/usr/share/freeswitch/sounds/welcome.wav";

    private static PlayNodeHandler NewHandler(ITtsStreamingService? tts = null, IConfiguration? config = null)
    {
        config ??= new ConfigurationBuilder().AddInMemoryCollection().Build();
        tts ??= NoStreamingProvider();
        return new PlayNodeHandler(new Mock<ITenantDbContextFactory>().Object, tts, config, NullLogger<PlayNodeHandler>.Instance);
    }

    private static ITtsStreamingService NoStreamingProvider()
    {
        var tts = new Mock<ITtsStreamingService>();
        tts.Setup(t => t.ResolveProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((TtsStreamingProviderInfo?)null);
        return tts.Object;
    }

    private static Mock<ITtsStreamingService> StreamingProviderConfigured()
    {
        var tts = new Mock<ITtsStreamingService>();
        tts.Setup(t => t.ResolveProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new TtsStreamingProviderInfo("elevenlabs", null));
        tts.Setup(t => t.PrepareStreamUrlAsync(It.IsAny<string>(), It.IsAny<TtsStreamingProviderInfo>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync("shout://relay.example/tts-mp3/tok-1");
        return tts;
    }

    private static TelephonyFlowContext Ctx(IEslCommander? esl) => new()
    {
        ChannelUuid = Uuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
        TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
        TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
        Esl = esl,
    };

    private static Mock<IEslCommander> NewEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.TransferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        return esl;
    }

    private static JsonObject FileNode() => new()
    {
        ["type"] = "tf_play",
        ["audioSource"] = "file",
        ["audioFileId"] = BuiltinFile,
        ["transitions"] = new JsonObject { ["tts_finished"] = "node_next", ["default"] = "node_next" },
    };

    // ── File playback ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileSource_Broadcasts_AndStoresPlayState()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);

        var result = await NewHandler().ExecuteAsync(FileNode(), ctx);

        Assert.Null(result.NextNodeId);
        Assert.Equal("playing", result.TransitionTaken);
        esl.Verify(e => e.BroadcastAsync(Uuid, "/usr/share/freeswitch/sounds/welcome.wav", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("/usr/share/freeswitch/sounds/welcome.wav", ctx.Vars["_play_media_arg"]);
        Assert.Equal("false", ctx.Vars["_play_loop"]);
        Assert.Equal("file", ctx.Vars["_play_audio_source"]);
        Assert.Equal("main", ctx.Vars["_play_state"]);
    }

    [Fact]
    public async Task FileNotResolvable_ReturnsError_NoBroadcast()
    {
        var esl = NewEsl();
        var node = FileNode();
        node["audioFileId"] = "";

        var result = await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        Assert.Equal("error", result.TransitionTaken);
        esl.Verify(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoEsl_ReturnsError_NoThrow()
    {
        var result = await NewHandler().ExecuteAsync(FileNode(), Ctx(esl: null));
        Assert.Equal("error", result.TransitionTaken);
        Assert.Null(result.NextNodeId);
    }

    [Fact]
    public async Task StartOffset_AppendsOffsetSuffixToMediaArg()
    {
        var esl = NewEsl();
        var node = FileNode();
        node["startOffsetSeconds"] = 5;

        await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.BroadcastAsync(Uuid, "/usr/share/freeswitch/sounds/welcome.wav@@5000", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AutoRestart_StoresLoopFlag()
    {
        var esl = NewEsl();
        var node = FileNode();
        node["autoRestart"] = true;
        var ctx = Ctx(esl.Object);

        await NewHandler().ExecuteAsync(node, ctx);

        Assert.Equal("true", ctx.Vars["_play_loop"]);
    }

    [Fact]
    public async Task DurationSeconds_WhenPositive_IsStored()
    {
        var esl = NewEsl();
        var node = FileNode();
        node["durationSeconds"] = 30;
        var ctx = Ctx(esl.Object);

        await NewHandler().ExecuteAsync(node, ctx);

        Assert.Equal("30", ctx.Vars["_play_duration_seconds"]);
    }

    [Fact]
    public async Task DurationSeconds_WhenZero_IsNotStored()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);

        await NewHandler().ExecuteAsync(FileNode(), ctx);

        Assert.False(ctx.Vars.ContainsKey("_play_duration_seconds"));
    }

    [Fact]
    public async Task PeriodicAnnouncements_ResolvedAndStoredAsJson()
    {
        var esl = NewEsl();
        var node = FileNode();
        node["autoRestart"] = true;
        node["periodicAnnouncements"] = new JsonArray { new JsonObject { ["fileId"] = BuiltinFile } };
        node["periodicAnnouncementIntervalSeconds"] = 45;
        var ctx = Ctx(esl.Object);

        await NewHandler().ExecuteAsync(node, ctx);

        var announcements = JsonSerializer.Deserialize<List<string>>(ctx.Vars["_play_announcements_json"]);
        Assert.Single(announcements!);
        Assert.Equal("/usr/share/freeswitch/sounds/welcome.wav", announcements![0]);
        Assert.Equal("0", ctx.Vars["_play_announcement_index"]);
        Assert.Equal("45", ctx.Vars["_play_announcement_interval"]);
    }

    [Fact]
    public async Task StoreTransitions_WritesPlayNextVarPerTransitionKey()
    {
        var esl = NewEsl();
        var node = FileNode();
        node["transitions"] = new JsonObject { ["tts_finished"] = "node_a", ["duration_reached"] = "node_b" };
        var ctx = Ctx(esl.Object);

        await NewHandler().ExecuteAsync(node, ctx);

        Assert.Equal("node_a", ctx.Vars["_play_next_tts_finished"]);
        Assert.Equal("node_b", ctx.Vars["_play_next_duration_reached"]);
    }

    [Fact]
    public async Task LeadInSilence_BroadcastsSilenceBeforeMainMedia()
    {
        var esl = NewEsl();
        var callOrder = new List<string>();
        esl.Setup(e => e.BroadcastAsync(Uuid, It.Is<string>(a => a.StartsWith("silence_stream://")), It.IsAny<CancellationToken>()))
           .Callback(() => callOrder.Add("silence"))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.BroadcastAsync(Uuid, "/usr/share/freeswitch/sounds/welcome.wav", It.IsAny<CancellationToken>()))
           .Callback(() => callOrder.Add("main"))
           .Returns(Task.CompletedTask);

        var node = FileNode();
        node["leadInSilenceMs"] = 10;

        await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        Assert.Equal(["silence", "main"], callOrder);
    }

    // ── Interrupt digits (build-order item 3) ───────────────────────────────────

    [Fact]
    public async Task InterruptDigits_WithInterruptedTransition_ArmsInterruptVars()
    {
        var esl = NewEsl();
        var node = FileNode();
        node["interruptDigits"] = "19";
        node["transitions"] = new JsonObject { ["default"] = "node_next", ["interrupted"] = "node_skip" };
        var ctx = Ctx(esl.Object);

        await NewHandler().ExecuteAsync(node, ctx);

        Assert.Equal("19", ctx.Vars["_play_interrupt_digits"]);
        Assert.Equal("node_skip", ctx.Vars["_play_interrupt_target"]);
    }

    [Fact]
    public async Task InterruptDigits_StripsCommasAndSpaces()
    {
        var esl = NewEsl();
        var node = FileNode();
        node["interruptDigits"] = "1, 9";
        node["transitions"] = new JsonObject { ["default"] = "node_next", ["interrupted"] = "node_skip" };
        var ctx = Ctx(esl.Object);

        await NewHandler().ExecuteAsync(node, ctx);

        Assert.Equal("19", ctx.Vars["_play_interrupt_digits"]);
    }

    [Fact]
    public async Task InterruptDigits_WithoutInterruptedTransition_IsNotArmed()
    {
        var esl = NewEsl();
        var node = FileNode();
        node["interruptDigits"] = "19";
        // No "interrupted" key wired — arming would dead-end a live interrupt press.
        var ctx = Ctx(esl.Object);

        await NewHandler().ExecuteAsync(node, ctx);

        Assert.False(ctx.Vars.ContainsKey("_play_interrupt_digits"));
        Assert.False(ctx.Vars.ContainsKey("_play_interrupt_target"));
    }

    [Fact]
    public async Task NoInterruptDigitsConfigured_IsNotArmed()
    {
        var esl = NewEsl();
        var node = FileNode();
        node["transitions"] = new JsonObject { ["default"] = "node_next", ["interrupted"] = "node_skip" };
        var ctx = Ctx(esl.Object);

        await NewHandler().ExecuteAsync(node, ctx);

        Assert.False(ctx.Vars.ContainsKey("_play_interrupt_digits"));
        Assert.False(ctx.Vars.ContainsKey("_play_interrupt_target"));
    }

    [Fact]
    public async Task InterruptDigits_LeftoverFromEarlierNode_IsExplicitlyCleared()
    {
        // A previous tf_play in the same call armed an interrupt; this node doesn't configure one
        // — its stale target must not survive to catch a digit press meant for THIS playback.
        var esl = NewEsl();
        var node = FileNode();
        var ctx = Ctx(esl.Object);
        ctx.Vars["_play_interrupt_digits"] = "5";
        ctx.Vars["_play_interrupt_target"] = "stale_node";

        await NewHandler().ExecuteAsync(node, ctx);

        Assert.False(ctx.Vars.ContainsKey("_play_interrupt_digits"));
        Assert.False(ctx.Vars.ContainsKey("_play_interrupt_target"));
    }

    // ── TTS via flite (no streaming provider configured) ───────────────────────

    private static JsonObject TtsNode(string text) => new()
    {
        ["type"] = "tf_play",
        ["audioSource"] = "tts",
        ["ttsText"] = text,
        ["ttsVoice"] = "kal",
        ["transitions"] = new JsonObject { ["tts_finished"] = "node_next" },
    };

    [Fact]
    public async Task Tts_NoStreamingProvider_UsesFliteSyntax_ViaChannelVar()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);

        var result = await NewHandler().ExecuteAsync(TtsNode("Hello there"), ctx);

        Assert.Equal("playing", result.TransitionTaken);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_tts_text", "Hello there", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.BroadcastAsync(Uuid, "tts://flite|kal|${cc_tts_text}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tts_TemplateTags_AreResolvedBeforeSynthesis_RegressionForS150Bug()
    {
        // The real, fixed bug: ttsText used to be passed straight to the provider/flite with zero
        // {{...}} interpolation — a phone-number readback literally said "flow.entered_phone".
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        ctx.Vars["entered_phone"] = "5416704541";

        await NewHandler().ExecuteAsync(TtsNode("Your number is {{flow.entered_phone}}"), ctx);

        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_tts_text", "Your number is 5416704541", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tts_NewlinesInText_AreSanitizedToSpaces()
    {
        var esl = NewEsl();
        await NewHandler().ExecuteAsync(TtsNode("Line one\nLine two"), Ctx(esl.Object));
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_tts_text", "Line one Line two", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tts_EmptyTextAfterResolve_SkipsSynthesis_FollowsTtsFinished()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        // Resolves to empty since the var was never set.
        var result = await NewHandler().ExecuteAsync(TtsNode("{{flow.never_set}}"), ctx);

        Assert.Equal("tts_finished", result.TransitionTaken);
        Assert.Equal("node_next", result.NextNodeId);
        esl.Verify(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── TTS via streaming vendor ────────────────────────────────────────────────

    [Fact]
    public async Task Tts_StreamingProviderConfigured_TransfersIntoTtsPlay_NoBroadcast()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        var tts = StreamingProviderConfigured();

        var result = await NewHandler(tts.Object).ExecuteAsync(TtsNode("Hello there"), ctx);

        Assert.Equal("playing", result.TransitionTaken);
        esl.Verify(e => e.SetChannelVarAsync(Uuid, "cc_tts_url", "shout://relay.example/tts-mp3/tok-1", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.TransferAsync(Uuid, "tts_play", "XML", "default", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("true", ctx.Vars["_tts_in_progress"]);
        Assert.Equal("node_next", ctx.Vars["_tts_next_finished"]);
    }

    [Fact]
    public async Task Tts_StreamingProvider_TemplateTagsStillResolvedBeforeSynthesis()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        ctx.Vars["entered_phone"] = "5416704541";
        var tts = StreamingProviderConfigured();

        await NewHandler(tts.Object).ExecuteAsync(TtsNode("Your number is {{flow.entered_phone}}"), ctx);

        tts.Verify(t => t.PrepareStreamUrlAsync("test-tenant", It.IsAny<TtsStreamingProviderInfo>(), "Your number is 5416704541", "kal", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tts_StreamingProvider_FallsBackThroughTransitionChain_WhenTtsFinishedNotWired()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        var tts = StreamingProviderConfigured();
        var node = TtsNode("Hi");
        node["transitions"] = new JsonObject { ["end_of_stream"] = "node_eos" };

        await NewHandler(tts.Object).ExecuteAsync(node, ctx);

        Assert.Equal("node_eos", ctx.Vars["_tts_next_finished"]);
    }

    [Fact]
    public async Task Tts_StreamingProvider_FallsBackToDefault_WhenNeitherTtsFinishedNorEndOfStreamWired()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        var tts = StreamingProviderConfigured();
        var node = TtsNode("Hi");
        node["transitions"] = new JsonObject { ["default"] = "node_default" };

        await NewHandler(tts.Object).ExecuteAsync(node, ctx);

        Assert.Equal("node_default", ctx.Vars["_tts_next_finished"]);
    }
}
