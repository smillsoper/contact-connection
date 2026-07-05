using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class TelEndNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_end";

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
                await ctx.Esl.BridgeChannelsAsync(ctx.ChannelUuid, agentUuid, ct);
            }
            ctx.Vars.Remove("_agent_uuid");
        }
        else if (!ctx.Vars.TryGetValue("_answered", out _) && !ctx.Vars.TryGetValue("_queued", out _))
        {
            // Call was never answered or queued — reject cleanly.
            await ctx.Esl!.HangupChannelAsync(ctx.ChannelUuid, ct);
        }

        return new TelephonyNodeResult(null, "end");
    }
}
