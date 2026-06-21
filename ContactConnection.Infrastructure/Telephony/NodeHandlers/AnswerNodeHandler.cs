using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class AnswerNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_answer";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        await ctx.Esl.AnswerChannelAsync(ctx.ChannelUuid, ct);
        ctx.Vars["_answered"] = "true";

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId);
    }
}
