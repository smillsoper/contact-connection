using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// The post-call fulfillment artifact for a single call. Created from the call record's
/// active cart at order commit time; inventory reservations are confirmed on creation.
///
/// An Order is immutable after creation (prices, quantities) — only fulfillment state
/// (status, tracking, timestamps) is updated afterward.
/// </summary>
public class Order
{
    // Identity
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    /// <summary>Null for subscription-generated orders (no call record).</summary>
    public Guid? CallRecordId { get; private set; }
    /// <summary>The interaction whose script this belongs to (S178). Null on rows from before interaction-scoped
    /// commerce that the backfill couldn't place, and on autoship orders (no call).</summary>
    public Guid? InteractionId { get; private set; }
    public void SetInteraction(Guid interactionId) => InteractionId = interactionId;

    // Lifecycle
    public string Status { get; private set; } = OrderStatus.Confirmed;

    // Financial snapshot — captured from CartDocument at commit time
    public decimal Subtotal { get; private set; }
    public decimal Shipping { get; private set; }
    public decimal SalesTax { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Total { get; private set; }

    // Cart-level shipping
    public string? ShipMethod { get; private set; }
    public string? ShippingZip { get; private set; }

    // Payment plan snapshot — JSONB (mirrors CartDocument.PaymentBreakdowns)
    public List<CartPaymentBreakdown> PaymentBreakdowns { get; private set; } = [];

    // Fulfillment timestamps
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    // Audit
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    private readonly List<OrderLine> _lines = [];
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    // Required by EF Core
    private Order() { }

    /// <summary>
    /// Creates an order from a confirmed cart. All line items must be provided.
    /// Caller is responsible for calling IInventoryService.ConfirmCartAsync before this.
    /// </summary>
    public static Order CreateFromCart(
        Guid id,
        Guid tenantId,
        Guid? callRecordId,
        CartDocument cart,
        List<OrderLine> lines)
    {
        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            Id               = id,
            TenantId         = tenantId,
            CallRecordId     = callRecordId,
            Status           = OrderStatus.Confirmed,
            Subtotal         = cart.CartSubtotal,
            Shipping         = cart.Shipping,
            SalesTax         = cart.SalesTax,
            Discount         = cart.Discount,
            Total            = cart.CartTotal,
            ShipMethod       = cart.ShipMethod,
            ShippingZip      = cart.ShippingZip,
            PaymentBreakdowns = cart.PaymentBreakdowns,
            CreatedAt        = now,
            UpdatedAt        = now
        };
        order._lines.AddRange(lines);
        return order;
    }

    /// <summary>
    /// Creates a subscription renewal order (no call record).
    /// Caller is responsible for inventory confirmation before calling this.
    /// </summary>
    public static Order CreateFromSubscription(
        Guid id,
        Guid tenantId,
        decimal subtotal,
        decimal shipping,
        decimal salesTax,
        decimal total,
        List<OrderLine> lines)
    {
        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            Id           = id,
            TenantId     = tenantId,
            CallRecordId = null,        // no call — autoship
            Status       = OrderStatus.Confirmed,
            Subtotal     = subtotal,
            Shipping     = shipping,
            SalesTax     = salesTax,
            Discount     = 0,
            Total        = total,
            CreatedAt    = now,
            UpdatedAt    = now
        };
        order._lines.AddRange(lines);
        return order;
    }

    /// <summary>
    /// Marks the order as cancelled. Only valid when no lines have shipped.
    /// </summary>
    public void Cancel()
    {
        Status      = OrderStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        UpdatedAt   = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Recalculates the order-level status from the current line statuses.
    /// Called after any line fulfillment update.
    /// </summary>
    public void RefreshStatus()
    {
        if (Status == OrderStatus.Cancelled) return;

        var statuses = _lines.Select(l => l.FulfillmentStatus).ToList();
        if (statuses.All(s => s == OrderLineStatus.Cancelled))
        {
            Status = OrderStatus.Cancelled;
        }
        else if (statuses.All(s => s == OrderLineStatus.Delivered || s == OrderLineStatus.Cancelled))
        {
            Status      = OrderStatus.Delivered;
            DeliveredAt ??= DateTimeOffset.UtcNow;
            ShippedAt   ??= DateTimeOffset.UtcNow;
        }
        else if (statuses.All(s => s == OrderLineStatus.Shipped
                                || s == OrderLineStatus.Delivered
                                || s == OrderLineStatus.Cancelled))
        {
            Status    = OrderStatus.Shipped;
            ShippedAt ??= DateTimeOffset.UtcNow;
        }
        else if (statuses.Any(s => s == OrderLineStatus.Shipped || s == OrderLineStatus.Delivered))
        {
            Status    = OrderStatus.PartiallyShipped;
            ShippedAt ??= DateTimeOffset.UtcNow;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Order lifecycle statuses.</summary>
public static class OrderStatus
{
    public const string Confirmed        = "confirmed";
    public const string PartiallyShipped = "partially_shipped";
    public const string Shipped          = "shipped";
    public const string Delivered        = "delivered";
    public const string Cancelled        = "cancelled";
}
