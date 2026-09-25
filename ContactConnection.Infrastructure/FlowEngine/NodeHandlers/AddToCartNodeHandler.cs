using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "add_to_cart" nodes — adds (or, in "replace" mode, swaps in) a specific pre-picked
/// Offer on the current call's cart via ICartService. Transparent to the agent; the offer/quantity
/// are fixed at design time, so this executes and advances immediately — any yes/no or tier-choice
/// branching happens upstream via ordinary input/branch nodes wired to this node.
///
/// Node schema:
/// {
///   "type": "add_to_cart",
///   "offerId": "guid",
///   "quantity": 1,
///   "mode": "add" | "replace",
///   "replacesOfferIds": ["guid", ...],   // only meaningful when mode = "replace"
///   "transitions": { "added": "node_010", "failed": "node_011" }
/// }
/// </summary>
public class AddToCartNodeHandler(IVariableResolver resolver, ICartService cart)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "add_to_cart";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var offerIdStr = Str(node, "offerId");
        var quantity = (int?)node["quantity"] ?? 1;
        var mode = Str(node, "mode") ?? "add";

        string transitionKey;

        if (string.IsNullOrEmpty(offerIdStr) || !Guid.TryParse(offerIdStr, out var offerId))
        {
            transitionKey = "failed";
        }
        else
        {
            try
            {
                var result = mode == "replace"
                    ? await cart.ReplaceItemsAsync(ctx.CallRecordId, GuidList(node, "replacesOfferIds"), offerId, quantity, ct)
                    : await cart.AddItemAsync(ctx.CallRecordId, offerId, quantity, ct);

                transitionKey = result.Succeeded ? "added" : "failed";
            }
            catch (InvalidOperationException)
            {
                transitionKey = "failed";
            }
        }

        var next = Transition(node, transitionKey);
        AppendHistory(ctx, node, input: null, transition: transitionKey);

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
