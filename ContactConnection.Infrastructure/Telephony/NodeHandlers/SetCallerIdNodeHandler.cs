using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Sets the outbound effective caller ID on the FreeSWITCH channel.
/// Node data: { "callerIdValue": "+15035551234" | "{{caller.ani}}" | "{{flow.var}}" }
/// </summary>
public class SetCallerIdNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_set_caller_id";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var rawValue = node["callerIdValue"]?.GetValue<string>() ?? "";

        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            var resolved = TelSetVariableNodeHandler.Resolve(rawValue, ctx);
            if (!string.IsNullOrWhiteSpace(resolved))
                await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "effective_caller_id_number", resolved, ct);
        }

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, "default");
    }
}
