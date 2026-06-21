using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class HangupNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_hangup";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        await ctx.Esl.HangupChannelAsync(ctx.ChannelUuid, ct);
        return new TelephonyNodeResult(null, "hangup");
    }
}
