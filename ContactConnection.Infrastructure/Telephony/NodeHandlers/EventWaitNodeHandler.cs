using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Entry point for the agent_selected event branch.
/// Fires when an agent is selected/assigned to the call (before they answer).
/// </summary>
public class OnAgentSelectedNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_on_agent_selected";

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var next = node["transitions"]?["default"]?.GetValue<string>();
        return Task.FromResult(new TelephonyNodeResult(next));
    }
}
