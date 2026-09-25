using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Infrastructure.Commerce;

/// <summary>
/// See <see cref="ICartService"/>. All four public methods funnel through
/// <see cref="ApplyAsync"/> — release the call record's current reservations, attempt to reserve
/// the mutated cart, price it, and save — the same sequence the original whole-document PUT
/// endpoint used, now the one place it happens.
/// </summary>
public class CartService : ICartService
{
    private readonly ICallRecordRepository _callRecords;
    private readonly IOfferRepository _offers;
    private readonly IInventoryService _inventory;
    private readonly IPricingService _pricing;

    public CartService(
        ICallRecordRepository callRecords,
        IOfferRepository offers,
        IInventoryService inventory,
        IPricingService pricing)
    {
        _callRecords = callRecords;
        _offers = offers;
        _inventory = inventory;
        _pricing = pricing;
    }

    public async Task<CartOperationResult> ReplaceCartAsync(Guid callRecordId, CartDocument newCart, CancellationToken ct = default)
    {
        var record = await LoadRecordAsync(callRecordId, ct);
        return await ApplyAsync(record, newCart, ct);
    }

    public async Task<CartOperationResult> AddItemAsync(Guid callRecordId, Guid offerId, int quantity, CancellationToken ct = default)
    {
        if (quantity < 1) throw new InvalidOperationException("Quantity must be at least 1.");

        var record = await LoadRecordAsync(callRecordId, ct);
        var offer = await _offers.GetByIdAsync(offerId, ct)
            ?? throw new InvalidOperationException($"Offer {offerId} not found");

        var existingItems = record.Cart?.Items ?? [];
        var newItem = BuildPricedItem(offer, quantity, existingItems);

        var newCart = (record.Cart ?? CartDocument.Empty()) with { Items = [.. existingItems, newItem] };
        return await ApplyAsync(record, newCart, ct);
    }

    public async Task<CartOperationResult> ReplaceItemsAsync(Guid callRecordId, IReadOnlyList<Guid> removeOfferIds, Guid addOfferId, int quantity, CancellationToken ct = default)
    {
        if (quantity < 1) throw new InvalidOperationException("Quantity must be at least 1.");

        var record = await LoadRecordAsync(callRecordId, ct);
        var offer = await _offers.GetByIdAsync(addOfferId, ct)
            ?? throw new InvalidOperationException($"Offer {addOfferId} not found");

        var removeSet = removeOfferIds.ToHashSet();
        var survivingItems = (record.Cart?.Items ?? []).Where(i => !removeSet.Contains(i.OfferId)).ToList();
        var newItem = BuildPricedItem(offer, quantity, survivingItems);

        var newCart = (record.Cart ?? CartDocument.Empty()) with { Items = [.. survivingItems, newItem] };
        return await ApplyAsync(record, newCart, ct);
    }

    public async Task<CartOperationResult> RemoveOffersAsync(Guid callRecordId, IReadOnlyList<Guid> offerIds, CancellationToken ct = default)
    {
        var record = await LoadRecordAsync(callRecordId, ct);
        var removeSet = offerIds.ToHashSet();
        var newItems = (record.Cart?.Items ?? []).Where(i => !removeSet.Contains(i.OfferId)).ToList();

        var newCart = (record.Cart ?? CartDocument.Empty()) with { Items = newItems };
        return await ApplyAsync(record, newCart, ct);
    }

    public async Task<CartOperationResult> RemoveItemAsync(Guid callRecordId, int itemIndex, CancellationToken ct = default)
    {
        var record = await LoadRecordAsync(callRecordId, ct);
        var items = record.Cart?.Items ?? [];
        if (itemIndex < 0 || itemIndex >= items.Count)
            throw new InvalidOperationException($"Item index {itemIndex} is out of range (cart has {items.Count} item(s)).");

        var newItems = items.Where((_, i) => i != itemIndex).ToList();
        var newCart = record.Cart! with { Items = newItems };
        return await ApplyAsync(record, newCart, ct);
    }

    public async Task<CartOperationResult> UpdateQuantityAsync(Guid callRecordId, int itemIndex, int quantity, CancellationToken ct = default)
    {
        if (quantity < 1) throw new InvalidOperationException("Quantity must be at least 1 — use RemoveItemAsync to remove an item.");

        var record = await LoadRecordAsync(callRecordId, ct);
        var items = record.Cart?.Items ?? [];
        if (itemIndex < 0 || itemIndex >= items.Count)
            throw new InvalidOperationException($"Item index {itemIndex} is out of range (cart has {items.Count} item(s)).");

        var target = items[itemIndex];
        var offer = await _offers.GetByIdAsync(target.OfferId, ct)
            ?? throw new InvalidOperationException($"Offer {target.OfferId} not found");

        var otherItems = items.Where((_, i) => i != itemIndex).ToList();
        var repriced = BuildPricedItem(offer, quantity, otherItems);

        var newItems = items.ToList();
        newItems[itemIndex] = repriced;
        var newCart = record.Cart! with { Items = newItems };
        return await ApplyAsync(record, newCart, ct);
    }

    // ── Shared helpers ──────────────────────────────────────────────────────

    private async Task<CallRecord> LoadRecordAsync(Guid callRecordId, CancellationToken ct)
        => await _callRecords.GetByIdWithInteractionsAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found");

    private async Task<CartOperationResult> ApplyAsync(CallRecord record, CartDocument newCart, CancellationToken ct)
    {
        await _inventory.ReleaseCartAsync(record.Cart, ct);

        var unavailable = await _inventory.ReserveCartAsync(newCart, ct);
        if (unavailable.Count > 0)
        {
            // Restore the old cart's reservations so the call record is left consistent.
            if (record.Cart is not null)
                await _inventory.ReserveCartAsync(record.Cart, ct);

            return CartOperationResult.Conflict(unavailable);
        }

        var calculated = await _pricing.CalculateTotalsAsync(newCart, ct);
        record.SetCart(calculated);
        await _callRecords.SaveChangesAsync(ct);

        return CartOperationResult.Success(calculated);
    }

    /// <summary>
    /// Snapshots an Offer/Product's current pricing fields into a new priced CartItem.
    /// See ICartService.AddItemAsync's doc comment for what's deliberately not populated yet.
    /// </summary>
    private CartItem BuildPricedItem(Offer offer, int quantity, IReadOnlyList<CartItem> otherItems)
    {
        var payments = _pricing.ResolvePayments(offer, quantity, otherItems);
        var unitPrice = payments.Sum(p => p.Amount);

        return new CartItem(
            OfferId: offer.Id,
            ProductId: offer.ProductId,
            Sku: offer.Product.Sku,
            Description: offer.Product.Description,
            Quantity: quantity,
            FullPrice: offer.FullPrice,
            ExtendedPrice: unitPrice * quantity,
            Shipping: offer.Shipping,
            Weight: offer.Product.Weight,
            SalesTax: 0,          // tax is computed cart-wide by PricingService.CalculateTotalsAsync, never per-item
            ShippingExempt: offer.ShippingExempt,
            TaxExempt: offer.TaxExempt,
            OnBackOrder: offer.Product.InventoryStatus == ProductInventoryStatus.CanBackorder,
            AutoShip: offer.AutoShip,
            AutoShipIntervalDays: 0,   // which interval applies is its own agent choice — not wired yet
            IsUpsell: offer.IsUpsell,
            UpsellQty: offer.UpsellQty,
            MixMatchCode: offer.MixMatchCode,
            ShipMethod: null,
            DeliveryMessage: null,
            ShipToJson: null,
            Payments: payments,
            PersonalizationAnswers: [],
            KitSelections: [],
            CanadaSurcharge: 0,
            AKHISurcharge: 0,
            OutlyingUSSurcharge: 0,
            ForeignSurcharge: 0);
    }
}
