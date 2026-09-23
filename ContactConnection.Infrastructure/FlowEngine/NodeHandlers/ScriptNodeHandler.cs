using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "script" nodes — agent reads resolved text content.
/// No input captured. Agent advances by acknowledging (clicking Next/Continue).
/// Transitions: always "default".
/// </summary>
public class ScriptNodeHandler(IVariableResolver resolver) : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "script";

    public Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var varCtx  = ctx.ToVariableContext();
        var content = Resolver.ResolveForDisplay(Str(node, "content") ?? string.Empty, varCtx);
        var next    = Transition(node, agentTransition) ?? Transition(node, "default");

        AppendHistory(ctx, node, input: null, transition: next);

        var state = BuildState(ctx, node, resolvedContent: content);
        return Task.FromResult(new NodeResult(state, next));
    }
}
