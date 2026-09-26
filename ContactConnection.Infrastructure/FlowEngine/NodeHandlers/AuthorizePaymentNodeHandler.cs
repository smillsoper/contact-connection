using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "authorize_payment" nodes — reads the tf_secure_collect card data already captured
/// (encrypted) on this call's CallRecord.SensitiveData and sends an auth-only transaction to the
/// configured gateway. Field-key names are configurable because tf_secure_collect's own field keys
/// are freeform per flow (see IPaymentService.AuthorizeAsync's doc comment). Transparent to the
/// agent; executes and advances immediately.
///
/// Node schema:
/// {
///   "type": "authorize_payment",
///   "provider": "authorize_net",
///   "cardNumberField": "card_number", "expField": "exp", "cvvField": "cvv",
///   "zipField": "zip",                          // a tf_secure_collect field key, OR
///   "zipField": "{{flow.billing_address.zip}}", // a {{...}} variable reference — zip isn't
///                                                // sensitive, so unlike card/exp/cvv (always
///                                                // read from the encrypted blob only) it can
///                                                // come from wherever the flow already has it,
///                                                // e.g. an earlier address node's output.
///   "amountMode": "cart_total" | "fixed", "fixedAmount": 19.95,
///   "outputVariable": "payment_result", // optional — see below
///   "transitions": { "approved": "node_x", "declined": "node_y", "error": "node_z" }
/// }
///
/// When outputVariable is set, the result is written into flow variables the same way api_call
/// does: the whole result as JSON under {{flow.outputVariable}}, plus each field flattened —
/// {{flow.outputVariable.status}}, {{flow.outputVariable.responseReasonText}} (the decline/error
/// message — the only way a downstream script node can tell the caller *why*),
/// {{flow.outputVariable.gatewayTransactionId}} (the Authorize.Net transaction id — this is the one
/// field a future Order API submission step will need), {{flow.outputVariable.authCode}}.
/// </summary>
public class AuthorizePaymentNodeHandler(IVariableResolver resolver, IPaymentService payments)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "authorize_payment";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var provider = Str(node, "provider") ?? "authorize_net";
        var cardNumberField = Str(node, "cardNumberField") ?? "card_number";
        var expField = Str(node, "expField") ?? "exp";
        var cvvField = Str(node, "cvvField") ?? "cvv";
        var zipField = Str(node, "zipField");
        var amountMode = Str(node, "amountMode") ?? "cart_total";
        var fixedAmount = amountMode == "fixed" ? (decimal?)node["fixedAmount"] : null;

        // zipField doubles as either a tf_secure_collect field key or a {{...}} variable reference
        // (see class doc comment) — resolve it here if it looks like a template, since this handler
        // is the one with access to IVariableResolver; IPaymentService takes the resolved value.
        string? zipOverride = zipField is not null && zipField.Contains("{{")
            ? Resolver.Resolve(zipField, ctx.ToVariableContext())
            : null;

        string transitionKey;
        PaymentAuthResult? result = null;
        try
        {
            result = await payments.AuthorizeAsync(
                ctx.CallRecordId, provider, cardNumberField, expField, cvvField, zipField, zipOverride, fixedAmount, ct);
            transitionKey = result.Status switch
            {
                PaymentTransactionStatus.Approved => "approved",
                PaymentTransactionStatus.Declined => "declined",
                _ => "error",
            };
        }
        catch (InvalidOperationException ex)
        {
            transitionKey = "error";
            result = new PaymentAuthResult(false, PaymentTransactionStatus.Error, null, null, null, ex.Message);
        }

        var outputVariable = Str(node, "outputVariable")?.Trim();
        if (!string.IsNullOrEmpty(outputVariable))
        {
            ctx.FlowVars[outputVariable] = JsonSerializer.Serialize(result);
            ctx.FlowVars[$"{outputVariable}.succeeded"] = result.Succeeded.ToString();
            ctx.FlowVars[$"{outputVariable}.status"] = result.Status;
            ctx.FlowVars[$"{outputVariable}.transactionId"] = result.TransactionId?.ToString() ?? "";
            ctx.FlowVars[$"{outputVariable}.gatewayTransactionId"] = result.GatewayTransactionId ?? "";
            ctx.FlowVars[$"{outputVariable}.authCode"] = result.AuthCode ?? "";
            ctx.FlowVars[$"{outputVariable}.responseReasonText"] = result.ResponseReasonText ?? "";
        }

        var next = Transition(node, transitionKey);
        AppendHistory(ctx, node, input: null, transition: transitionKey);

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
