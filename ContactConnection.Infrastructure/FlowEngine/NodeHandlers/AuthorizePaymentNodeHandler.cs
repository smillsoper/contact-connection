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
/// field a future Order API submission step will need), {{flow.outputVariable.authCode}},
/// {{flow.outputVariable.orderNumber}} (the call's order number, empty if the client has no
/// order-number sequence — also exposed as {{call_record.order_number}} once assigned),
/// {{flow.outputVariable.action}} (authorized | reauthorized | already_authorized),
/// {{flow.outputVariable.amount}}, {{flow.outputVariable.cardLast4}}.
///
/// Safe to reach more than once: same amount and card capture → no gateway call ("approved",
/// already_authorized); different amount or a newly captured card → the old authorization is voided
/// first, then a new one requested (a failed void stops there — "error" — rather than holding twice).
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
                ctx.CallRecordId, provider, cardNumberField, expField, cvvField, zipField, zipOverride, fixedAmount, ct,
                ctx.InteractionId);
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
            ctx.FlowVars[$"{outputVariable}.orderNumber"] = result.OrderNumber ?? "";
            ctx.FlowVars[$"{outputVariable}.action"] = result.Action;
            ctx.FlowVars[$"{outputVariable}.amount"] = result.Amount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "";
            ctx.FlowVars[$"{outputVariable}.cardLast4"] = result.CardLast4 ?? "";
        }

        // The authorization may have just assigned the call's order number — make it visible to
        // the rest of this flow without waiting for a context reload.
        if (!string.IsNullOrEmpty(result.OrderNumber))
            ctx.CallRecord["order_number"] = result.OrderNumber;

        var next = Transition(node, transitionKey);
        AppendHistory(ctx, node, input: null, transition: transitionKey);

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
