using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "get_value" nodes — reads a value back from the generic tenant/client/campaign
/// key-value store (IStoredValueService) into a flow variable. Transparent to the agent; executes
/// and advances immediately. No value stored yet (or expired) resolves to "" — not a failure,
/// there's only one exit.
///
/// Node schema:
/// {
///   "type": "get_value",
///   "scope": "tenant" | "client" | "campaign",
///   "keyName": "{{caller.phone}}_lastOrderId",
///   "outputVariable": "lastOrderId",
///   "transitions": { "default": "node_010" }
/// }
/// </summary>
public class GetValueNodeHandler(IVariableResolver resolver, IStoredValueService storedValues)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "get_value";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var varCtx = ctx.ToVariableContext();
        var scope = Str(node, "scope") ?? "tenant";
        var rawKey = Str(node, "keyName") ?? "";
        var outputVariable = Str(node, "outputVariable")?.Trim();

        if (!string.IsNullOrEmpty(outputVariable))
        {
            var keyName = Resolver.Resolve(rawKey, varCtx).Trim();
            var value = !string.IsNullOrEmpty(keyName)
                ? await storedValues.GetAsync(ctx.CallRecordId, scope, keyName, ct)
                : null;
            ctx.FlowVars[outputVariable] = value ?? string.Empty;
        }

        var next = Transition(node, "default");
        AppendHistory(ctx, node, input: null, transition: "default");

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
