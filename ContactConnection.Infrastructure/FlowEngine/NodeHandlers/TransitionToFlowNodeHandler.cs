using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "transition_to_flow" nodes — replaces the current flow with a target flow.
///
/// All FlowVars carry over into the target flow automatically (shared variable store).
/// Execution never returns to the calling flow — this is a one-way handoff.
///
/// Node schema:
/// {
///   "type": "transition_to_flow",
///   "label": "Go to Order Flow",
///   "targetFlowId": "guid",
///   "targetFlowName": "Order Flow",
///   "transitions": {}
/// }
/// </summary>
public class TransitionToFlowNodeHandler(IVariableResolver resolver, IFlowRepository flows)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "transition_to_flow";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var targetFlowId = Str(node, "targetFlowId");
        if (!Guid.TryParse(targetFlowId, out var flowId))
            throw new InvalidOperationException("transition_to_flow node is missing a valid targetFlowId.");

        var targetFlow = await flows.GetByIdAsync(flowId, ct)
            ?? throw new InvalidOperationException($"Target flow {flowId} not found.");

        if (!targetFlow.IsActive)
            throw new InvalidOperationException($"Target flow '{targetFlow.Name}' is not published.");

        var targetDefinition = JsonNode.Parse(targetFlow.Definition)?.AsObject()
            ?? throw new InvalidOperationException("Target flow definition is invalid JSON.");

        var entryNodeId = targetDefinition["entry_node"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Target flow definition has no entry_node.");

        // Replace the active definition — FlowVars carry over automatically via shared context
        ctx.FlowDefinition = targetDefinition;

        AppendHistory(ctx, node, null, entryNodeId);
        return new NodeResult(BuildState(ctx, node, resolvedContent: string.Empty), entryNodeId);
    }
}
