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

    public static void Apply(FlowExecutionContext ctx, string role, AddressData address)
    {
        var json = CallAddressJson.ToJsonObject(address).ToJsonString();
        if (role is CallAddressRole.Billing or CallAddressRole.BillingAndShipping)
            ctx.CallRecord[Billing] = json;
        if (role is CallAddressRole.Shipping or CallAddressRole.BillingAndShipping)
            ctx.CallRecord[Shipping] = json;
    }
}
