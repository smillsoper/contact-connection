using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Result of a cart mutation. <see cref="Cart"/> is the recalculated, saved cart on success.
/// On an inventory conflict (<see cref="UnavailableSkus"/> non-empty), the mutation was rolled
/// back — the call record's stored cart and reservations are unchanged from before the call.
/// </summary>
public record CartOperationResult(CartDocument? Cart, IReadOnlyList<string> UnavailableSkus)
{
    public bool Succeeded => UnavailableSkus.Count == 0;

    public static CartOperationResult Success(CartDocument cart) => new(cart, []);
    public static CartOperationResult Conflict(IReadOnlyList<string> unavailableSkus) => new(null, unavailableSkus);
}

/// <summary>
/// Owns the release→reserve→price→save orchestration for a call record's cart — the single
/// place this happens, used by both the cart HTTP endpoints and (eventually) CRM flow node
/// handlers that need to mutate a cart mid-script.
/// </summary>
public interface ICartService
{
    /// <summary>
    /// Replaces the entire cart document (the original whole-document PUT behavior).
    /// Throws <see cref="InvalidOperationException"/> if the call record doesn't exist.
    /// </summary>
    Task<CartOperationResult> ReplaceCartAsync(Guid callRecordId, CartDocument newCart, CancellationToken ct = default);

    /// <summary>
    /// Adds one line item for the given offer/quantity to the existing cart, snapshotting the
    /// offer/product's current pricing fields onto the new <c>CartItem</c>. Does not attempt to
    /// merge into an existing line for the same offer — always appends a new line.
    ///
    /// Not yet populated on the new item (documented gap, not silently wrong): personalization
    /// answers, kit selections, and geographic surcharges — all need their own agent-input step
    /// or address-classification logic not wired up yet.
    ///
    /// Throws <see cref="InvalidOperationException"/> if the call record or offer doesn't exist.
    /// </summary>
    Task<CartOperationResult> AddItemAsync(Guid callRecordId, Guid offerId, int quantity, CancellationToken ct = default);

    /// <summary>
    /// Removes every existing line whose OfferId is in <paramref name="removeOfferIds"/> (0, 1, or
    /// many matches — all removed), then adds one new line for <paramref name="addOfferId"/>, all
    /// in a single release→reserve→price→save pass. Backs a script's "upsell replaces the
    /// previously-added item(s)" case — e.g. a bundle offer superseding a base item plus an add-on
    /// added by earlier nodes — as distinct from an Offer's MixMatchCode, which prices existing and
    /// new items together as a group rather than removing anything.
    ///
    /// Throws <see cref="InvalidOperationException"/> if the call record or the offer being added
    /// doesn't exist. An empty or not-found <paramref name="removeOfferIds"/> entry is a no-op for
    /// that id, not an error — there's nothing wrong with "replacing" an item that was never added.
    /// </summary>
    Task<CartOperationResult> ReplaceItemsAsync(Guid callRecordId, IReadOnlyList<Guid> removeOfferIds, Guid addOfferId, int quantity, CancellationToken ct = default);

    /// <summary>
    /// Removes every existing line whose OfferId is in <paramref name="offerIds"/> — no add step,
    /// unlike <see cref="ReplaceItemsAsync"/>. Backs a script's "remove this item" node. A
    /// not-found id is a no-op for that id, not an error.
    ///
    /// Throws <see cref="InvalidOperationException"/> if the call record doesn't exist.
    /// </summary>
    Task<CartOperationResult> RemoveOffersAsync(Guid callRecordId, IReadOnlyList<Guid> offerIds, CancellationToken ct = default);

    /// <summary>
    /// Removes the item at <paramref name="itemIndex"/> (0-based, into <c>CartDocument.Items</c>).
    /// Throws <see cref="InvalidOperationException"/> if the call record doesn't exist or the
    /// index is out of range.
    /// </summary>
    Task<CartOperationResult> RemoveItemAsync(Guid callRecordId, int itemIndex, CancellationToken ct = default);

    /// <summary>
    /// Updates the quantity of the item at <paramref name="itemIndex"/>, re-resolving its payment
    /// schedule (a quantity change can cross a QPB/MixMatch threshold). Throws
    /// <see cref="InvalidOperationException"/> if the call record doesn't exist, the index is out
    /// of range, or <paramref name="quantity"/> is less than 1 (use RemoveItemAsync to remove).
    /// </summary>
    Task<CartOperationResult> UpdateQuantityAsync(Guid callRecordId, int itemIndex, int quantity, CancellationToken ct = default);
}
