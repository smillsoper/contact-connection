using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "remove_cart_item" nodes — removes one or more specific pre-picked Offers from the
/// current call's cart via ICartService, with no add step (see "add_to_cart" for that). Transitions
/// "removed"/"failed" are kept symmetric with add_to_cart even though removal itself can't fail —
/// re-reserving the surviving items after the removal can still hit a race-condition inventory
/// conflict. Transparent to the agent; executes and advances immediately.
///
/// Node schema:
/// {
///   "type": "remove_cart_item",
///   "removeOfferIds": ["guid", ...],
///   "transitions": { "removed": "node_010", "failed": "node_011" }
/// }
/// </summary>
public class RemoveCartItemNodeHandler(IVariableResolver resolver, ICartService cart)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "remove_cart_item";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        string transitionKey;
        try
        {
            var result = await cart.RemoveOffersAsync(ctx.CallRecordId, GuidList(node, "removeOfferIds"), ct);
            transitionKey = result.Succeeded ? "removed" : "failed";
        }
        catch (InvalidOperationException)
        {
            transitionKey = "failed";
        }

        var next = Transition(node, transitionKey);
        AppendHistory(ctx, node, input: null, transition: transitionKey);

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
