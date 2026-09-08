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
/// Covers WhisperNodeHandler's audioSource branch (Session 125): "file" resolves a media arg and
/// broadcasts it on the agent leg; "tts" speaks free text via flite on the agent leg (no vendor
/// streaming here — see the handler docstring); empty tts text skips the whisper and resumes.
/// </summary>
public class WhisperNodeHandlerTests
{
    private const string CallerUuid = "caller-uuid-1";
    private const string AgentUuid = "agent-uuid-1";

    private static WhisperNodeHandler NewHandler(Mock<ITtsStreamingService>? tts = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        return new WhisperNodeHandler(
            new Mock<ITenantDbContextFactory>().Object,
            (tts ?? new Mock<ITtsStreamingService>()).Object,
            config, NullLogger<WhisperNodeHandler>.Instance);
    }

    private static TelephonyFlowContext Ctx(IEslCommander? esl, bool withAgent = true)
    {
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = CallerUuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
            TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
            TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
            Esl = esl,
        };
        if (withAgent) ctx.Vars["_agent_uuid"] = AgentUuid;
        return ctx;
    }

    private static Mock<IEslCommander> NewEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.SetChannelVarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        esl.Setup(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        return esl;
    }

    private static JsonObject Node(string audioSource) => new()
    {
        ["type"] = "tf_whisper",
        ["audioSource"] = audioSource,
        ["transitions"] = new JsonObject { ["default"] = "node_end" },
    };

    [Fact]
    public async Task File_ResolvesBuiltin_AndBroadcastsOnAgentLeg()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        var node = Node("file");
        node["audioFileId"] = "__builtin:/usr/share/freeswitch/sounds/whisper.wav";

        var result = await NewHandler().ExecuteAsync(node, ctx);

        Assert.Equal("whisper_playing", result.TransitionTaken);
        Assert.Equal("node_end", ctx.Vars["_whisper_next_default"]);
        esl.Verify(e => e.BroadcastAsync(
            AgentUuid, "/usr/share/freeswitch/sounds/whisper.wav", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tts_SpeaksFliteOnAgentLeg_ViaChannelVar()
    {
        var esl = NewEsl();
        var ctx = Ctx(esl.Object);
        var node = Node("tts");
        node["ttsText"] = "Call from the sales line\nplease stand by";
        node["ttsVoice"] = "slt";

        var result = await NewHandler().ExecuteAsync(node, ctx);

        Assert.Equal("whisper_playing", result.TransitionTaken);
        // Newline collapsed to a space; text goes through cc_tts_text on the AGENT channel.
        esl.Verify(e => e.SetChannelVarAsync(
            AgentUuid, "cc_tts_text", "Call from the sales line please stand by", It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.BroadcastAsync(
            AgentUuid, "tts://flite|slt|${cc_tts_text}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tts_DefaultsVoiceToKal()
    {
        var esl = NewEsl();
        var node = Node("tts");
        node["ttsText"] = "hello agent";

        await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        esl.Verify(e => e.BroadcastAsync(
            AgentUuid, "tts://flite|kal|${cc_tts_text}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tts_StreamingVendorConfigured_BroadcastsShoutUrlOnAgentLeg()
    {
        var esl = NewEsl();
        var tts = new Mock<ITtsStreamingService>();
        tts.Setup(t => t.ResolveProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TtsStreamingProviderInfo("elevenlabs", null));
        tts.Setup(t => t.PrepareStreamUrlAsync("test-tenant", It.IsAny<TtsStreamingProviderInfo>(), "hello agent", "voice-xyz", It.IsAny<CancellationToken>()))
            .ReturnsAsync("shout://host.docker.internal:5135/relay/tts-mp3/abc123");

        var node = Node("tts");
        node["ttsText"] = "hello agent";
        node["ttsVoice"] = "voice-xyz";

        var result = await NewHandler(tts).ExecuteAsync(node, Ctx(esl.Object));

        Assert.Equal("whisper_playing", result.TransitionTaken);
        esl.Verify(e => e.BroadcastAsync(
            AgentUuid, "shout://host.docker.internal:5135/relay/tts-mp3/abc123", It.IsAny<CancellationToken>()), Times.Once);
        // No flite fallback when a vendor is configured.
        esl.Verify(e => e.SetChannelVarAsync(
            It.IsAny<string>(), "cc_tts_text", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Tts_EmptyText_SkipsWhisper_ResumesDefault()
    {
        var esl = NewEsl();
        var node = Node("tts");
        node["ttsText"] = "   ";

        var result = await NewHandler().ExecuteAsync(node, Ctx(esl.Object));

        Assert.Equal("node_end", result.NextNodeId);
        Assert.Equal("default", result.TransitionTaken);
        esl.Verify(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoAgentUuid_ReturnsError()
    {
        var result = await NewHandler().ExecuteAsync(Node("tts"), Ctx(NewEsl().Object, withAgent: false));
        Assert.Equal("error", result.TransitionTaken);
    }
}
