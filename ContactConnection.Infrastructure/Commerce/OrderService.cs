using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.Commerce;

public class OrderService : IOrderService
{
    private readonly ICallRecordRepository _callRecords;
    private readonly IOrderRepository _orders;
    private readonly IInventoryService _inventory;
    private readonly ISubscriptionRepository _subscriptions;

    public OrderService(
        ICallRecordRepository callRecords,
        IOrderRepository orders,
        IInventoryService inventory,
        ISubscriptionRepository subscriptions)
    {
        _callRecords   = callRecords;
        _orders        = orders;
        _inventory     = inventory;
        _subscriptions = subscriptions;
    }

    public async Task<(Order Order, bool Created)> CreateFromCartAsync(
        Guid callRecordId, CancellationToken ct = default, Guid? interactionId = null)
    {
        var callRecord = await _callRecords.GetByIdWithInteractionsAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found.");

        // One order per interaction (S178): a sales order and a CS order on the same call are separate. A legacy
        // record with no interactions keeps one order per call.
        var ix = callRecord.CommerceInteraction(interactionId);

        // Idempotent — return existing order if already committed
        var existing = ix is not null
            ? await _orders.GetByInteractionIdAsync(ix.Id, ct)
            : await _orders.GetByCallRecordIdAsync(callRecordId, ct);
        if (existing is not null)
            return (existing, Created: false);

        var cart = ix is null ? callRecord.Cart : ix.Cart;
        if (cart is null || cart.Items.Count == 0)
            throw new InvalidOperationException(
                $"Call record {callRecordId} has no active cart to commit.");

        // Convert soft reservations to real inventory decrements
        await _inventory.ConfirmCartAsync(cart, ct);

        // Pre-generate the order ID so lines can reference it before the order is persisted
        var orderId = Guid.NewGuid();

        var lines = cart.Items
            .Select(item => OrderLine.FromCartItem(orderId, callRecord.TenantId, item))
            .ToList();

        var order = Order.CreateFromCart(
            id:           orderId,
            tenantId:     callRecord.TenantId,
            callRecordId: callRecordId,
            cart:         cart,
            lines:        lines);

        if (ix is not null) order.SetInteraction(ix.Id);
        await _orders.AddAsync(order, ct);
        await _orders.SaveChangesAsync(ct);

        // Auto-enroll AutoShip lines into subscriptions
        var autoShipLines = lines.Where(l => l.AutoShip && l.AutoShipIntervalDays > 0).ToList();
        if (autoShipLines.Count > 0)
        {
            var subs = autoShipLines
                .Select(line => Subscription.CreateFromOrderLine(
                    tenantId:       callRecord.TenantId,
                    callRecordId:   callRecordId,
                    originalOrderId: orderId,
                    line:           line))
                .ToList();

            await _subscriptions.AddRangeAsync(subs, ct);
            await _subscriptions.SaveChangesAsync(ct);
        }

        return (order, Created: true);
    }
}
