using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "void_payment" nodes — voids the call's most recent approved, not-yet-voided
/// PaymentTransaction. No required config: there's normally at most one live authorization per
/// call, so no node-reference/picker mechanism is needed (see IPaymentService.VoidMostRecentAsync).
/// Useful right after an authorize_payment node, before the order is actually submitted — lets an
/// agent undo a transaction if the caller changes their mind, without the caller having to call back
/// later. Transparent to the agent; executes and advances immediately.
///
/// Node schema:
/// {
///   "type": "void_payment",
///   "outputVariable": "void_result", // optional — see below
///   "transitions": { "voided": "node_x", "failed": "node_y" }
/// }
///
/// When outputVariable is set (same convention as authorize_payment/api_call):
/// {{flow.outputVariable.responseReasonText}} carries the failure reason on "failed".
/// </summary>
public class VoidPaymentNodeHandler(IVariableResolver resolver, IPaymentService payments)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "void_payment";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var result = await payments.VoidMostRecentAsync(ctx.CallRecordId, ct);
        var transitionKey = result.Succeeded ? "voided" : "failed";

        var outputVariable = Str(node, "outputVariable")?.Trim();
        if (!string.IsNullOrEmpty(outputVariable))
        {
            ctx.FlowVars[outputVariable] = JsonSerializer.Serialize(result);
            ctx.FlowVars[$"{outputVariable}.succeeded"] = result.Succeeded.ToString();
            ctx.FlowVars[$"{outputVariable}.responseReasonText"] = result.ResponseReasonText ?? "";
        }

        var next = Transition(node, transitionKey);
        AppendHistory(ctx, node, input: null, transition: transitionKey);

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
