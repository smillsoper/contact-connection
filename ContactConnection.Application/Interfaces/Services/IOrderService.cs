using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Creates and manages orders from confirmed call record carts.
///
/// Lifecycle:
///   CreateFromCartAsync — validates the call has an active cart, confirms inventory
///   reservations (converts soft holds to real decrements), snapshots the cart into
///   Order + OrderLine records, and returns the created order.
///   Returns the existing order if one has already been created for the call.
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Creates an order from the current cart on the specified call record.
    /// Calls IInventoryService.ConfirmCartAsync internally.
    /// Returns (order, created=true) on success; (existingOrder, created=false) if already ordered.
    /// Throws InvalidOperationException if the call record has no cart or an empty cart.
    /// </summary>
    Task<(Order Order, bool Created)> CreateFromCartAsync(Guid callRecordId, CancellationToken ct = default, Guid? interactionId = null);
}
