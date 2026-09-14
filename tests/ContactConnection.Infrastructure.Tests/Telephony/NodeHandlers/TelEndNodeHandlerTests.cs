using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_end. Two responsibilities: (1) the whisper/agent_selected path's actual bridge point —
/// stop the caller's playback, play the agent connect tone (project_agent_connect_tone), then
/// uuid_bridge; (2) reject an unanswered/unqueued call cleanly. SettleMs is always 0 in these
/// tests (real Task.Delay would only add latency, nothing to assert on).
/// </summary>
public class TelEndNodeHandlerTests
{
    private const string CallerUuid = "caller-uuid-1";
    private const string AgentUuid  = "agent-uuid-1";

    private static TelEndNodeHandler NewHandler(bool? toneEnabled = null, string? toneStream = null)
    {
        var settings = new Dictionary<string, string?> { ["Telephony:AgentConnectTone:SettleMs"] = "0" };
        if (toneEnabled is { } en) settings["Telephony:AgentConnectTone:Enabled"] = en.ToString();
        if (toneStream is not null) settings["Telephony:AgentConnectTone:ToneStream"] = toneStream;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new TelEndNodeHandler(config, NullLogger<TelEndNodeHandler>.Instance);
    }

    private static TelephonyFlowContext Ctx(IEslCommander? esl, params (string k, string v)[] vars)
    {
        var ctx = new TelephonyFlowContext
        {
            ChannelUuid = CallerUuid, CallerNumber = "+15551110000", DestinationNumber = "+15552220000",
            TenantId = Guid.NewGuid(), CampaignId = Guid.NewGuid(), CallRecordId = Guid.NewGuid(),
            TenantSubdomain = "test-tenant", TenantSchemaName = "tenant_test_tenant", TenantTimezone = "America/Chicago",
            Esl = esl,
        };
        foreach (var (k, v) in vars) ctx.Vars[k] = v;
        return ctx;
    }

    private static Mock<IEslCommander> NewEsl()
    {
        var esl = new Mock<IEslCommander>();
        esl.Setup(e => e.BreakChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        esl.Setup(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        esl.Setup(e => e.BridgeChannelsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        esl.Setup(e => e.HangupChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return esl;
    }

    private static JsonObject Node() => new() { ["type"] = "tf_end" };

    [Fact]
    public async Task WhisperPath_BreaksCaller_PlaysTone_ThenBridges_InOrder()
    {
        var esl = NewEsl();
        var calls = new List<string>();
        esl.Setup(e => e.BreakChannelAsync(CallerUuid, It.IsAny<CancellationToken>()))
           .Callback(() => calls.Add("break")).Returns(Task.CompletedTask);
        esl.Setup(e => e.BroadcastAsync(AgentUuid, It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Callback(() => calls.Add("tone")).Returns(Task.CompletedTask);
        esl.Setup(e => e.BridgeChannelsAsync(CallerUuid, AgentUuid, It.IsAny<CancellationToken>()))
           .Callback(() => calls.Add("bridge")).Returns(Task.CompletedTask);

        var handler = NewHandler();
        var ctx = Ctx(esl.Object, ("_agent_uuid", AgentUuid));

        var result = await handler.ExecuteAsync(Node(), ctx);

        Assert.Equal(new[] { "break", "tone", "bridge" }, calls);
        Assert.Equal("end", result.TransitionTaken);
        Assert.False(ctx.Vars.ContainsKey("_agent_uuid"));   // consumed
        esl.Verify(e => e.BroadcastAsync(AgentUuid, "tone_stream://%(200,0,800)", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToneDisabled_SkipsBroadcast_StillBridges()
    {
        var esl = NewEsl();
        var handler = NewHandler(toneEnabled: false);
        var ctx = Ctx(esl.Object, ("_agent_uuid", AgentUuid));

        await handler.ExecuteAsync(Node(), ctx);

        esl.Verify(e => e.BroadcastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        esl.Verify(e => e.BridgeChannelsAsync(CallerUuid, AgentUuid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CustomToneStream_UsedVerbatim()
    {
        var esl = NewEsl();
        var handler = NewHandler(toneStream: "tone_stream://%(100,0,1200)");
        var ctx = Ctx(esl.Object, ("_agent_uuid", AgentUuid));

        await handler.ExecuteAsync(Node(), ctx);

        esl.Verify(e => e.BroadcastAsync(AgentUuid, "tone_stream://%(100,0,1200)", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToneBroadcastThrows_StillBridges()
    {
        var esl = NewEsl();
        esl.Setup(e => e.BroadcastAsync(AgentUuid, It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("channel gone"));

        var handler = NewHandler();
        var ctx = Ctx(esl.Object, ("_agent_uuid", AgentUuid));

        var result = await handler.ExecuteAsync(Node(), ctx);

        Assert.Equal("end", result.TransitionTaken);
        esl.Verify(e => e.BridgeChannelsAsync(CallerUuid, AgentUuid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoAgentUuid_UnansweredUnqueued_HangsUp()
    {
        var esl = NewEsl();
        var handler = NewHandler();
        var ctx = Ctx(esl.Object);   // no _agent_uuid, no _answered, no _queued

        var result = await handler.ExecuteAsync(Node(), ctx);

        esl.Verify(e => e.HangupChannelAsync(CallerUuid, It.IsAny<CancellationToken>()), Times.Once);
        esl.Verify(e => e.BridgeChannelsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("end", result.TransitionTaken);
    }

    [Fact]
    public async Task NoAgentUuid_ButAnswered_NoHangup_NoBridge()
    {
        var esl = NewEsl();
        var handler = NewHandler();
        var ctx = Ctx(esl.Object, ("_answered", "true"));

        await handler.ExecuteAsync(Node(), ctx);

        esl.Verify(e => e.HangupChannelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        esl.Verify(e => e.BridgeChannelsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
