using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class HangupNodeHandler(ILogger<HangupNodeHandler>? logger = null) : ITelephonyNodeHandler
{
    public string NodeType => "tf_hangup";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        // No ESL on this context (e.g. a trigger_telephony_event branch) — nothing to act on the channel with (S179).
        if (ctx.Esl is null)
            logger?.LogWarning("HangupNodeHandler [{Uuid}]: no ESL connection — can't hang up; ending the flow", ctx.ChannelUuid);
        else
            await ctx.Esl.HangupChannelAsync(ctx.ChannelUuid, ct);
        return new TelephonyNodeResult(null, "hangup");
    }
}
