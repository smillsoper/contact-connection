using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;
using ContactConnection.Infrastructure.FlowEngine;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// Builds the data a Liquid request-body template can read (see ILiquidTemplateRenderer):
///
///   flow, caller, agent, tenant, shared, input, api — the flow's variable namespaces (values that
///       hold JSON objects, like an address node's output, are exposed as objects)
///   call_record — call fields incl. order_number, dnis, contact_id_external, call_started_at,
///       billing_phone / shipping_phone (digits), and
///       billing_address / shipping_address objects (same camelCase keys as an address node's
///       output: firstName, lastName, address1, formattedAddress1, city, state, zip, ...)
///   cart — items[] (sku, description, quantity, full_price, unit_price, extended_price,
///       sales_tax, tax_code, tax_exempt, shipping, is_upsell, auto_ship, offer_id, product_id),
///       subtotal, shipping, shipping_tax, sales_tax, discount, fees[] (code, description,
///       amount), fee_total, total, ship_method, tax_status
///   payment — the call's approved, un-voided authorization: gateway, gateway_transaction_id,
///       auth_code, amount, card_last4, card_type, order_number (empty object if none)
///   now_utc — ISO-8601 UTC timestamp
///
/// Card data is never included — only what PaymentTransaction already stores (last 4, type).
/// </summary>
public interface IApiTemplateModelBuilder
{
    Task<JsonObject> BuildAsync(FlowExecutionContext ctx, CancellationToken ct = default);
}

public class ApiTemplateModelBuilder(
    ICallRecordRepository callRecords,
    IPaymentTransactionRepository payments) : IApiTemplateModelBuilder
{
    public async Task<JsonObject> BuildAsync(FlowExecutionContext ctx, CancellationToken ct = default)
    {
        // Interaction-scoped (S178): the script's own interaction's cart, order number and authorization.
        var record = await callRecords.GetByIdWithInteractionsAsync(ctx.CallRecordId, ct);
        var ix = record?.CommerceInteraction(ctx.InteractionId);
        var payment = record is null ? null : await payments.GetMostRecentApprovedAsync(record.Id, ix?.Id, ct);
        return Build(ctx, record, payment, DateTimeOffset.UtcNow, ix);
    }

    /// <summary>Pure assembly — internal for tests and for the builder UI's sample model.</summary>
    internal static JsonObject Build(FlowExecutionContext ctx, CallRecord? record, PaymentTransaction? payment, DateTimeOffset now,
        CallInteraction? interaction = null)
    {
        // The interaction's cart / order number (S178); without one given, the call's first interaction's.
        var commerce = interaction ?? record?.FirstInteraction;
        var cart = commerce?.Cart;
        var orderNumber = commerce?.OrderNumber;
        var callRecord = Namespace(ctx.CallRecord);
        if (record is not null)
        {
            callRecord["id"] = record.Id.ToString();
            callRecord["order_number"] = orderNumber ?? "";
            callRecord["dnis"] = record.Dnis ?? "";
            callRecord["caller_id"] = record.CallerId ?? "";
            callRecord["contact_id_external"] = record.ContactIdExternal ?? "";
            callRecord["email"] = record.Email ?? "";
            callRecord["billing_phone"] = record.BillingPhone ?? "";
            callRecord["shipping_phone"] = record.ShippingPhone ?? "";
            callRecord["call_started_at"] = record.CallStartAt?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) ?? "";
            callRecord["client_id"] = record.ClientId.ToString();
            callRecord["campaign_id"] = record.CampaignId.ToString();
            if (record.Addresses?.Billing is { } b) callRecord["billing_address"] = CallAddressJson.ToJsonObject(b);
            if (record.Addresses?.Shipping is { } s) callRecord["shipping_address"] = CallAddressJson.ToJsonObject(s);
        }

        return new JsonObject
        {
            ["flow"]        = Namespace(ctx.FlowVars),
            ["call_record"] = callRecord,
            ["caller"]      = Namespace(ctx.Caller),
            ["agent"]       = Namespace(ctx.Agent),
            ["tenant"]      = Namespace(ctx.Tenant),
            ["shared"]      = Namespace(ctx.SharedVars),
            ["input"]       = Namespace(ctx.Inputs),
            ["api"]         = Namespace(ctx.ApiResults),
            ["cart"]        = Cart(cart),
            ["payment"]     = Payment(payment),
            ["now_utc"]     = now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Liquid model for a caller that only has flat variables (the telephony engine — no
    /// cart or payment on a telephony call): { flow: vars, shared: sharedVars, now_utc }.</summary>
    public static JsonObject VariablesOnly(Dictionary<string, string> vars, Dictionary<string, string> sharedVars)
        => new()
        {
            ["flow"]    = Namespace(vars),
            ["shared"]  = Namespace(sharedVars),
            ["now_utc"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };

    /// <summary>A variable namespace as an object. Keys containing '.' are the flattened copies
    /// api_call/authorize_payment write alongside their JSON result ("x.status") — skipped, since
    /// the JSON object itself is exposed and Liquid reads x.status from it directly.</summary>
    private static JsonObject Namespace(Dictionary<string, string> vars)
    {
        var o = new JsonObject();
        foreach (var (key, value) in vars)
        {
            if (key.Contains('.')) continue;
            o[key] = ParseIfStructured(value);
        }
        return o;
    }

    private static JsonNode? ParseIfStructured(string value)
    {
        var t = value.TrimStart();
        if (t.StartsWith('{') || t.StartsWith('['))
        {
            try { return JsonNode.Parse(value); }
            catch (JsonException) { /* not JSON after all — keep the text */ }
        }
        return JsonValue.Create(value);
    }

    private static JsonObject Cart(CartDocument? cart)
    {
        cart ??= CartDocument.Empty();
        var items = new JsonArray();
        foreach (var i in cart.Items)
        {
            items.Add(new JsonObject
            {
                ["offer_id"]       = i.OfferId.ToString(),
                ["product_id"]     = i.ProductId.ToString(),
                ["sku"]            = i.Sku,
                ["description"]    = i.Description,
                ["quantity"]       = i.Quantity,
                ["full_price"]     = i.FullPrice,
                ["unit_price"]     = i.Quantity == 0 ? 0 : Math.Round(i.ExtendedPrice / i.Quantity, 2),
                ["extended_price"] = i.ExtendedPrice,
                ["sales_tax"]      = i.SalesTax,
                ["tax_code"]       = i.TaxCode ?? "",
                ["tax_exempt"]     = i.TaxExempt,
                ["shipping"]       = i.Shipping,
                ["is_upsell"]      = i.IsUpsell,
                ["auto_ship"]      = i.AutoShip,
            });
        }

        var fees = new JsonArray();
        foreach (var f in cart.Fees ?? [])
            fees.Add(new JsonObject { ["code"] = f.Code, ["description"] = f.Description, ["amount"] = f.Amount });

        return new JsonObject
        {
            ["items"]        = items,
            ["subtotal"]     = cart.CartSubtotal,
            ["shipping"]     = cart.Shipping,
            ["shipping_tax"] = cart.ShippingTax,
            ["sales_tax"]    = cart.SalesTax,
            ["discount"]     = cart.Discount,
            ["fees"]         = fees,
            ["fee_total"]    = (cart.Fees ?? []).Sum(f => f.Amount),
            ["total"]        = cart.CartTotal,
            ["ship_method"]  = cart.ShipMethod ?? "",
            ["tax_status"]   = cart.TaxStatus ?? "",
        };
    }

    private static JsonObject Payment(PaymentTransaction? p) => p is null ? new JsonObject() : new JsonObject
    {
        ["gateway"]                = p.Gateway,
        ["gateway_transaction_id"] = p.GatewayTransactionId ?? "",
        ["auth_code"]              = p.AuthCode ?? "",
        ["amount"]                 = p.Amount,
        ["card_last4"]             = p.CardLast4 ?? "",
        ["card_type"]              = p.CardType ?? "",
        ["order_number"]           = p.OrderNumber ?? "",
        ["status"]                 = p.Status,
        ["transaction_type"]       = p.TransactionType,
    };

    /// <summary>A representative model for the API builder's Preview/Test — same shape as a live
    /// call, filled with sample values (a Colorado order with a delivery fee and an approved auth).</summary>
    public static JsonObject Sample()
    {
        var ctx = new FlowExecutionContext
        {
            CallRecordId = Guid.Empty,
            FlowVars = new() { ["coupon_code"] = "", ["ReferrerFirstName"] = "Pat" },
            Caller = new() { ["first_name"] = "Jane", ["last_name"] = "Sample", ["email"] = "jane.sample@example.com", ["phone"] = "3035550100" },
            Agent = new() { ["id"] = Guid.Empty.ToString(), ["name"] = "Sample Agent" },
            Tenant = new() { ["id"] = Guid.Empty.ToString() },
        };
        var record = CallRecord.Create(Guid.Empty, Guid.Empty, Guid.Empty);
        var address = new AddressData
        {
            FirstName = "Jane", LastName = "Sample", Street = "1600 Broadway", City = "Denver",
            State = "CO", Zip = "80202", Country = "US", IsVerified = true,
        };
        record.SetAddresses(new CallAddresses { Billing = address, Shipping = address });
        record.SetDnis("8005550100");
        record.SetContactPhones("3035550100", "3035550199");
        var sample = record.AddInteraction(InteractionType.OrderSale);
        sample.SetOrderNumber("LIFSEA-10000123");

        var item = new CartItem(Guid.Empty, Guid.Empty, "283-1-CTY-P10-SN", "Neuro-Q 1 Bottle", 1, 49.95m, 49.95m,
            6.95m, 0m, 1.45m, false, false, false, false, 0, false, 0, null, "REG", null, null, [], [], [],
            0m, 0m, 0m, 0m, "PF050714");
        sample.SetCart(CartDocument.Empty() with
        {
            Items = [item], ShipMethod = "REG", CartSubtotal = 49.95m, Shipping = 6.95m, SalesTax = 1.65m,
            ShippingTax = 0.20m, CartTotal = 58.83m, TaxStatus = TaxCalculationStatus.Calculated,
            Fees = [new CartFee("CO_RDF", "Retail Delivery Fee", 0.28m)],
        });

        var payment = PaymentTransaction.Create(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty,
            "authorize_net", 58.83m, PaymentTransactionStatus.Approved, "60123456789", "ABC123", "1",
            "This transaction has been approved.", "Y", "M", "1111", "Visa", "LIFSEA-10000123");

        return Build(ctx, record, payment, new DateTimeOffset(2026, 9, 27, 17, 30, 0, TimeSpan.Zero), sample);
    }
}
