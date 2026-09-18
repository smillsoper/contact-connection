using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Telephony.NodeHandlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony.NodeHandlers;

/// <summary>
/// tf_end. Four responsibilities: (1) the whisper/agent_selected path's actual bridge point —
/// stop the caller's playback, play the agent connect tone (project_agent_connect_tone), then
/// uuid_bridge; (2) reject an unanswered/unqueued call cleanly; (3) push ReceiveTelephonyEventEnded
/// when a trigger_telephony_event branch reaches this node (_trig_wait_event_name pending) — the
/// correctly-timed signal for a CRM script's waitForTelephonyEventName auto-advance (see
/// project_shared_call_variables); (4) perform tf_secure_collect's deferred agent reconnect
/// (_sc_in_progress still pending — see EslBackgroundService.HandleSecureCollectDoneAsync) once the
/// branch containing the capture reaches its own end, instead of reconnecting immediately at
/// capture-completion (which raced/broke any downstream Play node). SettleMs is always 0 in these
/// tests (real Task.Delay would only add latency, nothing to assert on).
/// </summary>
public class TelEndNodeHandlerTests
{
    private const string CallerUuid = "caller-uuid-1";
    private const string AgentUuid  = "agent-uuid-1";

    private static TelEndNodeHandler NewHandler(
        bool? toneEnabled = null,
        string? toneStream = null,
        Mock<ITelephonyEventNotifier>? eventNotifier = null,
        Mock<ISecureCollectNotifier>? secureCollectNotifier = null,
        Mock<ITelephonyCallSessionStore>? sessionStore = null)
    {
        var settings = new Dictionary<string, string?> { ["Telephony:AgentConnectTone:SettleMs"] = "0" };
        if (toneEnabled is { } en) settings["Telephony:AgentConnectTone:Enabled"] = en.ToString();
        if (toneStream is not null) settings["Telephony:AgentConnectTone:ToneStream"] = toneStream;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new TelEndNodeHandler(
            (eventNotifier ?? new Mock<ITelephonyEventNotifier>()).Object,
            (secureCollectNotifier ?? new Mock<ISecureCollectNotifier>()).Object,
            (sessionStore ?? new Mock<ITelephonyCallSessionStore>()).Object,
            config, NullLogger<TelEndNodeHandler>.Instance);
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

    [Fact]
    public async Task PendingTelephonyEventWait_NotifiesEnded_AndClearsVars()
    {
        var notifier = new Mock<ITelephonyEventNotifier>();
        var handler = NewHandler(eventNotifier: notifier);
        var agentId = Guid.NewGuid();
        var ctx = Ctx(NewEsl().Object, ("_trig_wait_event_name", "cc_capture"), ("_trig_wait_agent_id", agentId.ToString()), ("_answered", "true"));

        await handler.ExecuteAsync(Node(), ctx);

        notifier.Verify(n => n.NotifyEndedAsync(
            agentId, ctx.CallRecordId, "cc_capture", "completed", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(ctx.Vars.ContainsKey("_trig_wait_event_name"));
        Assert.False(ctx.Vars.ContainsKey("_trig_wait_agent_id"));
    }

    [Fact]
    public async Task NoPendingTelephonyEventWait_DoesNotNotify()
    {
        var notifier = new Mock<ITelephonyEventNotifier>();
        var handler = NewHandler(eventNotifier: notifier);
        var ctx = Ctx(NewEsl().Object, ("_answered", "true"));

        await handler.ExecuteAsync(Node(), ctx);

        notifier.Verify(n => n.NotifyEndedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PendingTelephonyEventWait_NotifyThrows_StillCompletesNormally()
    {
        var notifier = new Mock<ITelephonyEventNotifier>();
        notifier.Setup(n => n.NotifyEndedAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub unreachable"));
        var handler = NewHandler(eventNotifier: notifier);
        var ctx = Ctx(NewEsl().Object, ("_trig_wait_event_name", "cc_capture"), ("_trig_wait_agent_id", Guid.NewGuid().ToString()), ("_answered", "true"));

        var result = await handler.ExecuteAsync(Node(), ctx);

        Assert.Equal("end", result.TransitionTaken);
        Assert.False(ctx.Vars.ContainsKey("_trig_wait_event_name"));
    }

    [Fact]
    public async Task PendingSecureCollectReconnect_BridgesPeer_NotifiesEnded_DeletesReverseKey_ClearsVars()
    {
        var scNotifier = new Mock<ISecureCollectNotifier>();
        var sessionStore = new Mock<ITelephonyCallSessionStore>();
        var esl = NewEsl();
        var handler = NewHandler(secureCollectNotifier: scNotifier, sessionStore: sessionStore);
        var agentId = Guid.NewGuid();
        var ctx = Ctx(esl.Object,
            ("_sc_in_progress", "true"), ("_sc_peer_uuid", AgentUuid), ("_sc_pending_outcome", "collected"),
            ("_assigned_agent_id", agentId.ToString()), ("_answered", "true"));

        await handler.ExecuteAsync(Node(), ctx);

        esl.Verify(e => e.BridgeChannelsAsync(CallerUuid, AgentUuid, It.IsAny<CancellationToken>()), Times.Once);
        scNotifier.Verify(n => n.NotifyEndedAsync(agentId, ctx.CallRecordId, "collected", It.IsAny<CancellationToken>()), Times.Once);
        sessionStore.Verify(s => s.DeleteKeyAsync($"sc_peer:{AgentUuid}", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(ctx.Vars.ContainsKey("_sc_in_progress"));
        Assert.False(ctx.Vars.ContainsKey("_sc_peer_uuid"));
        Assert.False(ctx.Vars.ContainsKey("_sc_pending_outcome"));
    }

    [Fact]
    public async Task PendingSecureCollectReconnect_DefaultsOutcomeToCollected_WhenMissing()
    {
        var scNotifier = new Mock<ISecureCollectNotifier>();
        var handler = NewHandler(secureCollectNotifier: scNotifier);
        var agentId = Guid.NewGuid();
        var ctx = Ctx(NewEsl().Object,
            ("_sc_in_progress", "true"), ("_sc_peer_uuid", AgentUuid),
            ("_assigned_agent_id", agentId.ToString()), ("_answered", "true"));

        await handler.ExecuteAsync(Node(), ctx);

        scNotifier.Verify(n => n.NotifyEndedAsync(agentId, ctx.CallRecordId, "collected", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoPendingSecureCollectReconnect_DoesNotBridgeOrNotify()
    {
        var scNotifier = new Mock<ISecureCollectNotifier>();
        var esl = NewEsl();
        var handler = NewHandler(secureCollectNotifier: scNotifier);
        var ctx = Ctx(esl.Object, ("_answered", "true"));

        await handler.ExecuteAsync(Node(), ctx);

        esl.Verify(e => e.BridgeChannelsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        scNotifier.Verify(n => n.NotifyEndedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PendingSecureCollectReconnect_BridgeThrows_StillNotifiesAndClearsVars()
    {
        var scNotifier = new Mock<ISecureCollectNotifier>();
        var esl = NewEsl();
        esl.Setup(e => e.BridgeChannelsAsync(CallerUuid, AgentUuid, It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("peer already gone"));
        var handler = NewHandler(secureCollectNotifier: scNotifier);
        var agentId = Guid.NewGuid();
        var ctx = Ctx(esl.Object,
            ("_sc_in_progress", "true"), ("_sc_peer_uuid", AgentUuid), ("_sc_pending_outcome", "failed"),
            ("_assigned_agent_id", agentId.ToString()), ("_answered", "true"));

        var result = await handler.ExecuteAsync(Node(), ctx);

        Assert.Equal("end", result.TransitionTaken);
        scNotifier.Verify(n => n.NotifyEndedAsync(agentId, ctx.CallRecordId, "failed", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(ctx.Vars.ContainsKey("_sc_in_progress"));
    }
}
