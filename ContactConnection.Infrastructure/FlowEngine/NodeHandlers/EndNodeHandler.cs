using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "end" nodes — terminates the flow with a final status.
/// Returns a terminal FlowNodeState; the engine marks the session complete.
///
/// Node schema:
/// {
///   "type": "end",
///   "label": "Call Complete",
///   "content": "Thank you, {{caller.first_name}}. Have a great day!",
///   "end_status": "complete" | "incomplete" | "abandoned"
/// }
/// </summary>
public class EndNodeHandler(IVariableResolver resolver) : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "end";

    public Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var varCtx  = ctx.ToVariableContext();
        var content = Resolver.ResolveForDisplay(Str(node, "content") ?? string.Empty, varCtx);

        AppendHistory(ctx, node, input: null, transition: null);

        var state = BuildState(ctx, node, resolvedContent: content, isTerminal: true);
        return Task.FromResult(new NodeResult(state, NextNodeId: null));
    }
}
