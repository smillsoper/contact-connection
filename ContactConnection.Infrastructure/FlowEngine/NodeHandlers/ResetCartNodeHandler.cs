using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "reset_cart" nodes — clears every item from the current call's cart via ICartService.
/// No configuration, no failure mode (releasing all reservations and reserving nothing can't
/// conflict) — a single "default" transition. Transparent to the agent; executes and advances
/// immediately.
///
/// Node schema:
/// {
///   "type": "reset_cart",
///   "transitions": { "default": "node_010" }
/// }
/// </summary>
public class ResetCartNodeHandler(IVariableResolver resolver, ICartService cart)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "reset_cart";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        await cart.ReplaceCartAsync(ctx.CallRecordId, CartDocument.Empty(), ct);

        var next = Transition(node, "default");
        AppendHistory(ctx, node, input: null, transition: "default");

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
