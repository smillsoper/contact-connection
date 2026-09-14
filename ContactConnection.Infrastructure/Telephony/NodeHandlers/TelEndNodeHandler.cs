using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class TelEndNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_end";

    private readonly IConfiguration _config;
    private readonly ILogger<TelEndNodeHandler> _logger;

    public TelEndNodeHandler(IConfiguration config, ILogger<TelEndNodeHandler> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
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
