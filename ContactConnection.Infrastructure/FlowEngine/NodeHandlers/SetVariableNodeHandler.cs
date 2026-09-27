using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using System.Collections.Generic;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "set_variable" nodes — assigns a value to a {{flow.*}} variable.
///
/// Two call-record targets are special: {{call_record.billing_address}} and
/// {{call_record.shipping_address}}. Assigning an address object to one (e.g. the output of an
/// address node, {{flow.billing_address}}) saves it onto the call record exactly as an address node
/// with that addressRole would — including re-pricing the cart when the shipping address changes.
/// Typical use: "ship to the billing address?" → yes →
///   { "variable": "{{call_record.shipping_address}}", "value": "{{flow.billing_address}}" }
/// {{call_record.billing_phone}} / {{call_record.shipping_phone}} work the same way for phone
/// nodes' output (or any phone string) — saved as digits on the call record — and
/// {{call_record.email}} for an email node's output (the customer email, also {{caller.email}}).
/// Transparent to the agent; executes and advances immediately.
/// Commonly used to extract and store api_call response fields for later use.
///
/// Node schema:
/// {
///   "type": "set_variable",
///   "label": "Store Customer ID",
///   "assignments": [
///     { "variable": "customerId", "value": "{{api.node_005.id}}" },
///     { "variable": "orderTotal",  "value": "{{api.node_005.total_price}}" }
///   ],
///   "transitions": { "default": "node_010" }
/// }
/// </summary>
public class SetVariableNodeHandler(
    IVariableResolver resolver, ISharedCallVariableStore sharedVars, ICallAddressService callAddresses)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "set_variable";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var varCtx      = ctx.ToVariableContext();
        var assignments = node["assignments"]?.AsArray();

        if (assignments is not null)
        {
            foreach (var item in assignments.OfType<JsonObject>())
            {
                var variable = item["variable"]?.GetValue<string>();
                var template = item["value"]?.GetValue<string>();

                if (variable is null || template is null) continue;

                var resolvedValue = Resolver.Resolve(template, varCtx);

                // Strip {{...}} wrapper if the variable name was entered as a tag
                var targetKey = variable.Trim();
                if (targetKey.StartsWith("{{") && targetKey.EndsWith("}}"))
                    targetKey = targetKey[2..^2].Trim();

                // Dispatch to the correct namespace context dictionary
                var dotIndex = targetKey.IndexOf('.');
                if (dotIndex > 0)
                {
                    var ns  = targetKey[..dotIndex].ToLowerInvariant();
                    var key = targetKey[(dotIndex + 1)..];
                    switch (ns)
                    {
                        case "caller": ctx.Caller[key]   = resolvedValue; break;
                        case "agent":  ctx.Agent[key]    = resolvedValue; break;
                        case "tenant": ctx.Tenant[key]   = resolvedValue; break;
                        case "flow":
                            // Nested write: flow.objectKey.propPath → merge into existing JSON object
                            var nestedDot = key.IndexOf('.');
                            if (nestedDot > 0)
                                SetNestedFlowVar(ctx.FlowVars, key[..nestedDot], key[(nestedDot + 1)..], resolvedValue);
                            else
                                ctx.FlowVars[key] = resolvedValue;
                            break;
                        case "call_record" when key.Equals(CallAddressVars.Billing, StringComparison.OrdinalIgnoreCase)
                                             || key.Equals(CallAddressVars.Shipping, StringComparison.OrdinalIgnoreCase):
                            // Not a plain variable write — persist to the call record. A value that
                            // isn't an address object (e.g. an unset variable) is ignored rather than
                            // wiping the call's existing address.
                            if (CallAddressJson.ToAddressData(resolvedValue) is { } address)
                            {
                                var role = key.Equals(CallAddressVars.Billing, StringComparison.OrdinalIgnoreCase)
                                    ? CallAddressRole.Billing : CallAddressRole.Shipping;
                                await callAddresses.SetAsync(ctx.CallRecordId, role, address, ct);
                                CallAddressVars.Apply(ctx, role, address);
                            }
                            break;
                        case "call_record" when key.Equals(CallAddressVars.Email, StringComparison.OrdinalIgnoreCase):
                            // The customer email (CallRecord.Email / {{caller.email}}) — from an email
                            // node's output object or a plain address; anything else is ignored.
                            if (CallAddressVars.EmailValue(resolvedValue) is { } emailValue)
                            {
                                await callAddresses.SetEmailAsync(ctx.CallRecordId, emailValue, ct);
                                CallAddressVars.ApplyEmail(ctx, emailValue);
                            }
                            break;
                        case "call_record" when key.Equals(CallAddressVars.BillingPhone, StringComparison.OrdinalIgnoreCase)
                                             || key.Equals(CallAddressVars.ShippingPhone, StringComparison.OrdinalIgnoreCase):
                            // Same idea as the address targets: persist the digits (from a phone
                            // node's output object or a plain phone string); an empty value is ignored.
                            if (CallAddressVars.PhoneDigits(resolvedValue) is { } phoneDigits)
                            {
                                var phoneRole = key.Equals(CallAddressVars.BillingPhone, StringComparison.OrdinalIgnoreCase)
                                    ? CallAddressRole.Billing : CallAddressRole.Shipping;
                                await callAddresses.SetPhoneAsync(ctx.CallRecordId, phoneRole, phoneDigits, ct);
                                CallAddressVars.ApplyPhone(ctx, phoneRole, phoneDigits);
                            }
                            break;
                        case "shared":
                            // Call-wide, visible to the telephony call flow for the same call too —
                            // written straight to the shared store, not ctx.FlowVars. No nested-JSON
                            // form (unlike flow.*) — keep the cross-engine contract to flat values.
                            ctx.SharedVars[key] = resolvedValue;
                            await sharedVars.SetAsync(ctx.CallRecordId, key, resolvedValue, ct);
                            break;
                        default: ctx.FlowVars[targetKey] = resolvedValue; break;
                    }
                }
                else
                {
                    ctx.FlowVars[targetKey] = resolvedValue;
                }
            }
        }

        var next  = Transition(node, agentTransition) ?? Transition(node, "default");
        AppendHistory(ctx, node, input: null, transition: next);

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }

    /// <summary>
    /// Merges a single property into a JSON object stored in FlowVars.
    /// flow.billing_address.firstName = "John" reads FlowVars["billing_address"],
    /// sets the firstName key, and writes the updated JSON back.
    /// Supports arbitrary depth: flow.obj.a.b.c navigates/creates nested objects.
    /// </summary>
    private static void SetNestedFlowVar(
        Dictionary<string, string> flowVars,
        string objectKey,
        string propPath,
        string value)
    {
        JsonObject root;
        if (flowVars.TryGetValue(objectKey, out var existing))
        {
            try   { root = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject(); }
            catch { root = new JsonObject(); }
        }
        else
        {
            root = new JsonObject();
        }

        // Navigate / create intermediate objects
        var segments = propPath.Split('.');
        var current  = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var seg = segments[i];
            if (current[seg] is not JsonObject child)
            {
                child = new JsonObject();
                current[seg] = child;
            }
            current = child;
        }

        // Set the leaf as a JSON string value
        current[segments[^1]] = JsonValue.Create(value);

        flowVars[objectKey] = root.ToJsonString();
    }
}
