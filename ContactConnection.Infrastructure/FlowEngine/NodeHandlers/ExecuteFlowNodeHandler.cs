using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "execute_flow" nodes — runs a published sub-flow inline within the current session.
///
/// All FlowVars are shared: the sub-flow reads and writes the same variable store.
/// When the sub-flow reaches its end node the call stack is popped and execution
/// resumes at the node connected to this node's default transition.
///
/// Node schema:
/// {
///   "type": "execute_flow",
///   "label": "Get Customer Info",
///   "targetFlowId": "guid",
///   "targetFlowName": "Customer Information Flow",
///   "transitions": { "default": "node_after_return" }
/// }
/// </summary>
public class ExecuteFlowNodeHandler(IVariableResolver resolver, IFlowRepository flows)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "execute_flow";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var targetFlowId = Str(node, "targetFlowId");
        if (!Guid.TryParse(targetFlowId, out var flowId))
            throw new InvalidOperationException("execute_flow node is missing a valid targetFlowId.");

        var subFlow = await flows.GetByIdAsync(flowId, ct)
            ?? throw new InvalidOperationException($"Sub-flow {flowId} not found.");

        if (!subFlow.IsActive)
            throw new InvalidOperationException($"Sub-flow '{subFlow.Name}' is not published.");

        var subDefinition = JsonNode.Parse(subFlow.Definition)?.AsObject()
            ?? throw new InvalidOperationException("Sub-flow definition is invalid JSON.");

        var subEntryNodeId = subDefinition["entry_node"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Sub-flow definition has no entry_node.");

        var returnNodeId = Transition(node, agentTransition) ?? Transition(node, "default")
            ?? throw new InvalidOperationException("execute_flow node has no default transition (return point).");

        // Push parent context — restored automatically when sub-flow terminates
        ctx.CallStack.Add(new FlowCallFrame(
            FlowId:        ctx.FlowId,
            DefinitionJson: ctx.FlowDefinition.ToJsonString(),
            ReturnNodeId:  returnNodeId));

        ctx.FlowDefinition = subDefinition;

        AppendHistory(ctx, node, null, subEntryNodeId);
        return new NodeResult(BuildState(ctx, node, resolvedContent: string.Empty), subEntryNodeId);
    }
}
