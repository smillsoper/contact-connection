using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Infrastructure.FlowEngine;

/// <summary>
/// Keeps {{call_record.billing_address}} / {{call_record.shipping_address}} in the running flow's
/// context in step with what was just saved to the call record, so later nodes in the same flow see
/// it without waiting for a context reload.
/// </summary>
public static class CallAddressVars
{
    public const string Billing  = "billing_address";
    public const string Shipping = "shipping_address";
    public const string Email = "email";
    public const string BillingPhone  = "billing_phone";
    public const string ShippingPhone = "shipping_phone";
    public const string FirstName = "first_name";
    public const string LastName  = "last_name";

    /// <summary>The customer name just saved to the call record — {{call_record.first_name}} /
    /// {{caller.first_name}} (and last_name, and {{caller.name}}) for the rest of the flow.</summary>
    public static void ApplyName(FlowExecutionContext ctx, string key, string value)
    {
        ctx.CallRecord[key] = value;
        ctx.Caller[key] = value;
        ctx.Caller["name"] = string.Join(" ", new[] { ctx.Caller.GetValueOrDefault(FirstName), ctx.Caller.GetValueOrDefault(LastName) }
            .Where(n => !string.IsNullOrWhiteSpace(n)));
    }

    public static void ApplyPhone(FlowExecutionContext ctx, string role, string phone)
    {
        if (role is CallAddressRole.Billing or CallAddressRole.BillingAndShipping)
            ctx.CallRecord[BillingPhone] = phone;
        if (role is CallAddressRole.Shipping or CallAddressRole.BillingAndShipping)
            ctx.CallRecord[ShippingPhone] = phone;
    }

    /// <summary>The customer email just saved to the call record — visible as both
    /// {{call_record.email}} and {{caller.email}} for the rest of the flow.</summary>
    public static void ApplyEmail(FlowExecutionContext ctx, string email)
    {
        ctx.CallRecord[Email] = email;
        ctx.Caller[Email] = email;
    }

    /// <summary>An email value — an email node's output object ({"value": "a@b.com", ...}) or a
    /// plain string. Null unless it looks like an email address.</summary>
    public static string? EmailValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.StartsWith('{'))
        {
            try { text = System.Text.Json.Nodes.JsonNode.Parse(text)?["value"]?.GetValue<string>()?.Trim() ?? ""; }
            catch (System.Text.Json.JsonException) { return null; }
        }
        return text.Contains('@') && !text.Any(char.IsWhiteSpace) ? text : null;
    }

    /// <summary>The digits of a phone value — a phone node's output object ({"value": "5415551234",
    /// ...}) or any plain phone string. Null when there are no digits (e.g. an unset variable).</summary>
    public static string? PhoneDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.StartsWith('{'))
        {
            try { text = System.Text.Json.Nodes.JsonNode.Parse(text)?["value"]?.GetValue<string>() ?? ""; }
            catch (System.Text.Json.JsonException) { return null; }
        }
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    public static void Apply(FlowExecutionContext ctx, string role, AddressData address)
    {
        var json = CallAddressJson.ToJsonObject(address).ToJsonString();
        if (role is CallAddressRole.Billing or CallAddressRole.BillingAndShipping)
            ctx.CallRecord[Billing] = json;
        if (role is CallAddressRole.Shipping or CallAddressRole.BillingAndShipping)
            ctx.CallRecord[Shipping] = json;
    }
}
