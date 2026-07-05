using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Aborts the outbound dial attempt and stores a message for the agent.
/// Terminal node — no transitions. The caller inspects ctx.IsCancelled after
/// ExecuteAsync returns to decide whether to proceed with the dial.
/// Node data: { "cancelMessage": "This number is only available during business hours." }
/// </summary>
public class CancelDialNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_cancel_dial";

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var rawMessage = node["cancelMessage"]?.GetValue<string>() ?? "";
        var resolved   = TelSetVariableNodeHandler.Resolve(rawMessage, ctx);
        ctx.Cancel(resolved);

        // Terminal — no next node
        return Task.FromResult(new TelephonyNodeResult(null, "cancelled"));
    }
}
