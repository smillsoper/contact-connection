using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Entry point for the agent_answer event branch.
/// Fires when an agent picks up the call (after the FreeSWITCH bridge is established).
/// ctx.AnsweringAgentId and ctx.InteractionId are set at this point.
/// ctx.FlowEngine is set when called from the HTTP request scope (AnswerQueuedCall endpoint).
/// </summary>
public class OnAgentAnswerNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_on_agent_answer";

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var next = node["transitions"]?["default"]?.GetValue<string>();
        return Task.FromResult(new TelephonyNodeResult(next));
    }
}
