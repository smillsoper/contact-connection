using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class TelEndNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_end";

    private readonly ITelephonyEventNotifier _eventNotifier;
    private readonly ISecureCollectNotifier _secureCollectNotifier;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly IConfiguration _config;
    private readonly ILogger<TelEndNodeHandler> _logger;

    public TelEndNodeHandler(
        ITelephonyEventNotifier eventNotifier,
        ISecureCollectNotifier secureCollectNotifier,
        ITelephonyCallSessionStore sessionStore,
        IConfiguration config,
        ILogger<TelEndNodeHandler> logger)
    {
        _eventNotifier = eventNotifier;
        _secureCollectNotifier = secureCollectNotifier;
        _sessionStore = sessionStore;
        _config = config;
        _logger = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        // A deferred tf_secure_collect reconnect (see EslBackgroundService.HandleSecureCollectDoneAsync's
        // "Common exit" comment) — the agent's parked leg is only reconnected once this branch
        // actually reaches its own end, not immediately at capture-completion, so a downstream
        // Play/announcement node isn't racing (or getting torn down by) the reconnect.
        if (ctx.Vars.GetValueOrDefault("_sc_in_progress") == "true")
        {
            var scPeerUuid = ctx.Vars.GetValueOrDefault("_sc_peer_uuid");
            var scOutcome  = ctx.Vars.GetValueOrDefault("_sc_pending_outcome", "collected");
            if (!string.IsNullOrEmpty(scPeerUuid))
            {
                await _sessionStore.DeleteKeyAsync($"sc_peer:{scPeerUuid}", ct);

                if (ctx.Esl is not null)
                {
                    try
                    {
                        await ctx.Esl.BridgeChannelsAsync(ctx.ChannelUuid, scPeerUuid, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "TelEndNodeHandler [{Uuid}]: deferred secure-collect reconnect of {Peer} failed",
                            ctx.ChannelUuid, scPeerUuid);
                    }
                }

                if (Guid.TryParse(ctx.Vars.GetValueOrDefault("_assigned_agent_id"), out var scAgentId))
                {
                    try
                    {
                        await _secureCollectNotifier.NotifyEndedAsync(scAgentId, ctx.CallRecordId, scOutcome, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "TelEndNodeHandler [{Uuid}]: deferred secure-collect-ended push failed",
                            ctx.ChannelUuid);
                    }
                }
            }
            ctx.RemoveSessionVar("_sc_in_progress");
            ctx.RemoveSessionVar("_sc_peer_uuid");
            ctx.RemoveSessionVar("_sc_rebridge");
            ctx.RemoveSessionVar("_sc_pending_outcome");
        }

        // A trigger_telephony_event branch reaching its own end — the correctly-timed signal for
        // a CRM script node waiting on this event's outcome (see ITelephonyEventNotifier). Fires
        // regardless of how many async hops (e.g. tf_play's PLAYBACK_STOP continuation) the branch
        // took to get here, since this handler runs in whichever segment actually reaches tf_end.
        if (ctx.Vars.TryGetValue("_trig_wait_event_name", out var waitEventName) && !string.IsNullOrEmpty(waitEventName))
        {
            if (Guid.TryParse(ctx.Vars.GetValueOrDefault("_trig_wait_agent_id"), out var waitAgentId))
            {
                try
                {
                    await _eventNotifier.NotifyEndedAsync(waitAgentId, ctx.CallRecordId, waitEventName, "completed", ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "TelEndNodeHandler [{Uuid}]: telephony-event-ended push failed for '{Event}' — flow continues regardless",
                        ctx.ChannelUuid, waitEventName);
                }
            }
            ctx.RemoveSessionVar("_trig_wait_event_name");
            ctx.RemoveSessionVar("_trig_wait_agent_id");
        }

        // Whisper path: caller and agent are parked separately — bridge them now.
        if (ctx.Vars.TryGetValue("_agent_uuid", out var agentUuid) && !string.IsNullOrEmpty(agentUuid))
        {
            if (ctx.Esl is not null)
            {
                // Stop any audio playing on the caller's channel (MOH loop, announcements, etc.)
                // so they don't hear queue audio bleed into the live conversation.
                await ctx.Esl.BreakChannelAsync(ctx.ChannelUuid, ct);
                // Agent connect tone — see EslClient.BridgeToAgentAsync's identical step for the
                // simple-bridge / direct-extension paths. This is the whisper/agent_selected
                // path's own bridge point (uuid_bridge called directly, not via BridgeToAgentAsync),
                // so the tone has to be played here rather than shared with that method.
                await PlayAgentConnectToneAsync(ctx.Esl, agentUuid, ct);
                await ctx.Esl.BridgeChannelsAsync(ctx.ChannelUuid, agentUuid, ct);
            }
            ctx.Vars.Remove("_agent_uuid");
        }
        else if (ctx.Esl is not null && !ctx.Vars.TryGetValue("_answered", out _) && !ctx.Vars.TryGetValue("_queued", out _))
        {
            // Call was never answered or queued — reject cleanly. No-op if the channel is
            // already gone (e.g. reached via the call_disconnected event branch, which runs
            // after CHANNEL_HANGUP and has no live ESL connection to command).
            await ctx.Esl.HangupChannelAsync(ctx.ChannelUuid, ct);
        }

        return new TelephonyNodeResult(null, "end");
    }

    /// <summary>
    /// A short beep in the AGENT's ear right before the caller is bridged in (predictive-dialer
    /// "zip tone" convention) — see project memory project_agent_connect_tone. Global on/off via
    /// <c>Telephony:AgentConnectTone:*</c> config (not per-tenant/campaign). Broadcast on the
    /// agent leg only, never the caller's; a broadcast failure is logged and swallowed — it must
    /// never block the actual bridge.
    /// </summary>
    private async Task PlayAgentConnectToneAsync(IEslCommander esl, string agentUuid, CancellationToken ct)
    {
        if (bool.TryParse(_config["Telephony:AgentConnectTone:Enabled"], out var enabled) && !enabled)
            return;

        var mediaArg = _config["Telephony:AgentConnectTone:ToneStream"];
        if (string.IsNullOrWhiteSpace(mediaArg)) mediaArg = "tone_stream://%(200,0,800)";
        var settleMs = int.TryParse(_config["Telephony:AgentConnectTone:SettleMs"], out var ms) && ms >= 0 ? ms : 250;

        try { await esl.BroadcastAsync(agentUuid, mediaArg, ct); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Agent connect tone broadcast failed for {AgentUuid} (non-fatal)", agentUuid);
        }
        if (settleMs > 0)
            await Task.Delay(settleMs, ct);
    }
}
