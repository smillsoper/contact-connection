namespace ContactConnection.Domain.Entities;

/// <summary>
/// The canonical domain object types a WebhookEndpoint's payload can be mapped onto. Deliberately
/// a small, curated list — every domain entity mutates only through its own named methods (never
/// raw reflection), so each type here has a matching curated set of match fields and operations
/// defined in CanonicalWebhookMappingEvaluator (backend) and constants/canonicalWebhookTypes.ts
/// (frontend). Not every type needs to support every shape — "not all canonical types have the
/// mapping feature" is satisfied by this catalog being curated, not a blanket property writer.
/// See API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support".
/// </summary>
public static class CanonicalWebhookType
{
    /// <summary>Match fields: Id, CallRecordId. Operations: Cancel. Primarily used as the parent
    /// record in the "items array" shape (see WebhookEndpoint.MappingConfig) — a fulfillment
    /// agency's payload updates several OrderLines under one Order.</summary>
    public const string Order = "order";

    /// <summary>Match fields: Id (flat shape — matches today's original fulfillment-tracking
    /// case), Sku (only meaningful as the child-match field inside an Order's items array, since
    /// Sku alone can't find a line without knowing which order). Operations: Ship, MarkDelivered,
    /// Cancel.</summary>
    public const string OrderLine = "order_line";

    /// <summary>Match fields: Id, ContactIdExternal. Operations: SetCustomField (drives the
    /// existing ICustomFieldService — no new domain/repository code needed for this type).</summary>
    public const string CallRecord = "call_record";

    public static readonly string[] All = [Order, OrderLine, CallRecord];

    public static bool IsValid(string canonicalType) => All.Contains(canonicalType);
}
