using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class TelEndNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_end";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        // If the call was neither answered nor queued, reject cleanly before FreeSWITCH decides.
        if (!ctx.Vars.TryGetValue("_answered", out _) && !ctx.Vars.TryGetValue("_queued", out _))
            await ctx.Esl.HangupChannelAsync(ctx.ChannelUuid, ct);

        return new TelephonyNodeResult(null, "end");
    }
}
