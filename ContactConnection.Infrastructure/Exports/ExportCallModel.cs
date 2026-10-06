using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.FlowEngine;
using ContactConnection.Infrastructure.Media;

namespace ContactConnection.Infrastructure.Exports;

/// <summary>
/// What an export template sees for one call (S180). Times are UTC ISO-8601 — <c>format_time</c> writes them in the export's
/// time zone. No card data: the payment object is what PaymentTransaction keeps (last 4, type, auth code, amount).
///
///   call: id, started_at, ended_at, handle_time_seconds, source, record_type, status, run_mode,
///         ani, area_code, dnis, first_name, last_name, email, phone, billing_phone, shipping_phone,
///         account_number, client_number, contact_id_external, fulfillment_status, tracking_number,
///         client{id,name}, campaign{id,name}, agent{id,name}, billing_address{…}, shipping_address{…},
///         media{agency, station, market_type, media_type, ad_type, phone_number, start_date, fields{…}},
///         custom_fields{…}, disposition, has_order, order_count, order_number, order_total, order_tax,
///         interactions[ { id, number, type, status, disposition, started_at, completed_at, agent{}, campaign{},
///                         order_number, order_submitted_at, has_order, total, tax, payment_status, tier_label,
///                         cart{items[…], subtotal, shipping, sales_tax, total…}, payment{…} } ]
/// </summary>
public static class ExportCallModel
{
    public sealed record Lookups(
        IReadOnlyDictionary<Guid, string> Clients,
        IReadOnlyDictionary<Guid, string> Campaigns,
        IReadOnlyDictionary<Guid, string> Agents,
        IReadOnlyDictionary<Guid, IReadOnlyList<ProductFlag>>? OfferFlags = null);

    public static JsonObject Build(CallRecord r, Lookups lk, IReadOnlyList<PaymentTransaction> payments)
    {
        var interactions = new JsonArray();
        var ordered = r.Interactions.OrderBy(i => i.InteractionNumber).ToList();
        foreach (var i in ordered) interactions.Add(Interaction(r, i, lk, payments));

        var orders = ordered.Where(HasOrder).ToList();
        var ani = r.CallerId ?? "";
        return new JsonObject
        {
            ["id"] = r.Id.ToString(),
            ["started_at"] = Time(r.CallStartAt),
            ["ended_at"] = Time(r.CallEndAt ?? r.DisconnectedAt),
            ["handle_time_seconds"] = r.HandleTimeSeconds,
            ["source"] = r.Source,
            ["record_type"] = r.RecordType,
            ["status"] = r.OverallStatus,
            ["run_mode"] = r.RunMode,
            ["ani"] = ani,
            ["area_code"] = AreaCode(ani),
            ["dnis"] = r.Dnis ?? "",
            ["first_name"] = r.FirstName ?? "",
            ["last_name"] = r.LastName ?? "",
            ["email"] = r.Email ?? "",
            ["phone"] = r.Phone ?? "",
            ["billing_phone"] = r.BillingPhone ?? "",
            ["shipping_phone"] = r.ShippingPhone ?? "",
            ["account_number"] = r.AccountNumber ?? "",
            ["client_number"] = r.ClientNumber ?? "",
            ["contact_id_external"] = r.ContactIdExternal ?? "",
            ["fulfillment_status"] = r.FulfillmentStatus ?? "",
            ["tracking_number"] = r.TrackingNumber ?? "",
            ["client"] = Named(r.ClientId, lk.Clients),
            ["campaign"] = Named(r.CampaignId, lk.Campaigns),
            ["agent"] = Named(r.AgentId, lk.Agents),
            ["billing_address"] = r.Addresses?.Billing is { } b ? CallAddressJson.ToJsonObject(b) : new JsonObject(),
            ["shipping_address"] = r.Addresses?.Shipping is { } s ? CallAddressJson.ToJsonObject(s) : new JsonObject(),
            ["media"] = r.MediaAttribution is { } m ? MediaAttributionJson.ToJson(m) : new JsonObject(),
            ["custom_fields"] = ParseObject(r.CustomFields),
            ["disposition"] = ordered.LastOrDefault(i => !string.IsNullOrEmpty(i.Disposition))?.Disposition ?? "",
            ["has_order"] = orders.Count > 0,
            ["order_count"] = orders.Count,
            ["order_number"] = ordered.FirstOrDefault(i => !string.IsNullOrEmpty(i.OrderNumber))?.OrderNumber ?? "",
            ["order_total"] = orders.Sum(i => i.TotalAmount ?? i.Cart?.CartTotal ?? 0m),
            ["order_tax"] = orders.Sum(i => i.TaxAmount ?? i.Cart?.SalesTax ?? 0m),
            ["interactions"] = interactions,
        };
    }

    private static bool HasOrder(CallInteraction i) => i.OrderSubmittedAt is not null;

    private static JsonObject Interaction(CallRecord r, CallInteraction i, Lookups lk, IReadOnlyList<PaymentTransaction> payments)
    {
        var payment = payments
            .Where(p => p.InteractionId == i.Id || (p.InteractionId is null && i.InteractionNumber == 1))
            .OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        return new JsonObject
        {
            ["id"] = i.Id.ToString(),
            ["number"] = i.InteractionNumber,
            ["type"] = i.Type,
            ["status"] = i.Status,
            ["disposition"] = i.Disposition ?? "",
            ["started_at"] = Time(i.StartedAt),
            ["completed_at"] = Time(i.CompletedAt),
            ["agent"] = Named(i.AgentId ?? r.AgentId, lk.Agents),
            ["campaign"] = Named(i.CampaignId is { } c && c != Guid.Empty ? c : r.CampaignId, lk.Campaigns),
            ["order_number"] = i.OrderNumber ?? "",
            ["order_submitted_at"] = Time(i.OrderSubmittedAt),
            ["has_order"] = HasOrder(i),
            ["total"] = i.TotalAmount ?? i.Cart?.CartTotal ?? 0m,
            ["tax"] = i.TaxAmount ?? i.Cart?.SalesTax ?? 0m,
            ["payment_status"] = i.PaymentStatus ?? "",
            ["tier_label"] = i.RoutedTierLabel ?? "",
            ["cart"] = CartWithFlags(i.Cart, lk),
            ["payment"] = payment is null ? new JsonObject() : new JsonObject
            {
                ["gateway"] = payment.Gateway,
                ["gateway_transaction_id"] = payment.GatewayTransactionId ?? "",
                ["auth_code"] = payment.AuthCode ?? "",
                ["amount"] = payment.Amount,
                ["card_last4"] = payment.CardLast4 ?? "",
                ["card_type"] = payment.CardType ?? "",
                ["order_number"] = payment.OrderNumber ?? "",
            },
        };
    }

    /// <summary>The API cart model, plus each line's offer flags (<c>line.flags["Cannella Order SKU"]</c>) — per-offer codes
    /// a vendor file needs live there.</summary>
    private static JsonObject CartWithFlags(CartDocument? cart, Lookups lk)
    {
        var json = ApiTemplateModelBuilder.Cart(cart);
        if (json["items"] is not JsonArray items) return json;
        foreach (var item in items.OfType<JsonObject>())
        {
            var flags = new JsonObject();
            if (Guid.TryParse(item["offer_id"]?.GetValue<string>(), out var offerId)
                && lk.OfferFlags?.GetValueOrDefault(offerId) is { } list)
                foreach (var f in list) flags[f.Name] = f.Value;
            item["flags"] = flags;
        }
        return json;
    }

    /// <summary>The area code of a North American number (a leading country code 1 is skipped).</summary>
    public static string AreaCode(string? phone)
    {
        var d = new string((phone ?? "").Where(char.IsAsciiDigit).ToArray());
        if (d.Length == 11 && d[0] == '1') d = d[1..];
        return d.Length >= 10 ? d[..3] : "";
    }

    public static string Time(DateTimeOffset? t) =>
        t?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) ?? "";

    private static JsonObject Named(Guid? id, IReadOnlyDictionary<Guid, string> names) =>
        id is { } g && g != Guid.Empty
            ? new JsonObject { ["id"] = g.ToString(), ["name"] = names.GetValueOrDefault(g, "") }
            : new JsonObject { ["id"] = "", ["name"] = "" };

    private static JsonObject ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try { return JsonNode.Parse(json) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }
}
