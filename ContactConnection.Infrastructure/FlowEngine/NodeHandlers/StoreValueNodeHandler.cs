using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.StoredValues;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "store_value" nodes — writes a resolved value into the generic tenant/client/campaign
/// key-value store (IStoredValueService), with an optional retention window. Deliberately separate
/// from SetCustomFieldNodeHandler: no pre-defined field, any key, any of the three scopes. See
/// ContactConnection.Domain.Entities.StoredValue. Transparent to the agent; executes and advances
/// immediately.
///
/// Node schema:
/// {
///   "type": "store_value",
///   "scope": "tenant" | "client" | "campaign",
///   "keyName": "{{caller.phone}}_lastOrderId",
///   "value": "{{api.node_005.orderId}}",
///   "retention": "forever" | "1_hour" | "24_hours" | "1_week" | "1_month",
///   "transitions": { "default": "node_010" }
/// }
/// </summary>
public class StoreValueNodeHandler(IVariableResolver resolver, IStoredValueService storedValues)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "store_value";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var varCtx = ctx.ToVariableContext();
        var scope = Str(node, "scope") ?? "tenant";
        var rawKey = Str(node, "keyName") ?? "";
        var rawValue = Str(node, "value") ?? "";

        var keyName = Resolver.Resolve(rawKey, varCtx).Trim();
        if (!string.IsNullOrEmpty(keyName))
        {
            var value = Resolver.Resolve(rawValue, varCtx);
            var expiresAt = RetentionOptions.ResolveExpiresAt(Str(node, "retention"));
            await storedValues.SetAsync(ctx.CallRecordId, scope, keyName, value, expiresAt, ct);
        }

        var next = Transition(node, agentTransition) ?? Transition(node, "default");
        AppendHistory(ctx, node, input: null, transition: next);

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
