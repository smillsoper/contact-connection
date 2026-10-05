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
    private readonly ICampaignRepository _campaigns;

    public CartService(
        ICallRecordRepository callRecords,
        IOfferRepository offers,
        IInventoryService inventory,
        IPricingService pricing,
        ICampaignRepository campaigns)
    {
        _callRecords = callRecords;
        _offers = offers;
        _inventory = inventory;
        _pricing = pricing;
        _campaigns = campaigns;
    }

    public async Task<CartOperationResult> RecalculateAsync(Guid callRecordId, CancellationToken ct = default, Guid? interactionId = null)
    {
        var record = await LoadRecordAsync(callRecordId, ct);

        // Items are unchanged, so reservations are too — just re-price and save. Without a specific interaction
        // (an address change), every interaction's cart is re-priced: the address belongs to the whole call.
        List<CallInteraction?> targets = interactionId is { } one && one != Guid.Empty
            ? [record.CommerceInteraction(one)]
            : [.. record.Interactions];
        CartDocument? last = null;
        foreach (var ix in targets)
        {
            var cart = CartOf(record, ix);
            if (cart is null) continue;
            last = await _pricing.CalculateTotalsAsync(cart, await BuildTaxContextAsync(record, ix, ct), ct);
            Store(record, ix, last);
        }
        if (last is null) return CartOperationResult.Success(CartDocument.Empty());
        await _callRecords.SaveChangesAsync(ct);
        return CartOperationResult.Success(interactionId is null ? CartOf(record, record.CommerceInteraction()) ?? last : last);
    }

    public async Task<CartOperationResult> ReplaceCartAsync(Guid callRecordId, CartDocument newCart, CancellationToken ct = default, Guid? interactionId = null)
    {
        var record = await LoadRecordAsync(callRecordId, ct);
        return await ApplyAsync(record, await ResolveAsync(record, interactionId, ct), newCart, ct);
    }

    public async Task<CartOperationResult> AddItemAsync(Guid callRecordId, Guid offerId, int quantity, CancellationToken ct = default, bool enforceScope = false, Guid? interactionId = null)
    {
        if (quantity < 1) throw new InvalidOperationException("Quantity must be at least 1.");

        var record = await LoadRecordAsync(callRecordId, ct);
        var ix = await ResolveAsync(record, interactionId, ct);
        var offer = await _offers.GetByIdAsync(offerId, ct)
            ?? throw new InvalidOperationException($"Offer {offerId} not found");
        if (enforceScope && !OfferFitsCall(offer, record, ix))
            throw new InvalidOperationException("This offer isn't available for this call's client/campaign.");

        var current = CartOf(record, ix);
        var existingItems = current?.Items ?? [];
        var newItem = BuildPricedItem(offer, quantity, existingItems);

        var newCart = (current ?? CartDocument.Empty()) with { Items = [.. existingItems, newItem] };
        return await ApplyAsync(record, ix, newCart, ct);
    }

    /// <summary>Same rule as IOfferRepository.GetAvailableForContextAsync: a tenant-wide offer fits any
    /// call; a client-scoped offer only fits that client's calls, and only its listed campaigns if any.</summary>
    /// The interaction's campaign decides (S178: a CS agent on a transferred call gets CS's offers).
    internal static bool OfferFitsCall(Offer offer, CallRecord record, CallInteraction? interaction = null)
    {
        var campaignId = interaction?.CampaignId ?? record.CampaignId;
        return offer.AvailableFor(record.ClientId == Guid.Empty ? null : record.ClientId,
                                  campaignId == Guid.Empty ? null : campaignId);
    }

    public async Task<CartOperationResult> ReplaceItemsAsync(Guid callRecordId, IReadOnlyList<Guid> removeOfferIds, Guid addOfferId, int quantity, CancellationToken ct = default, Guid? interactionId = null)
    {
        if (quantity < 1) throw new InvalidOperationException("Quantity must be at least 1.");

        var record = await LoadRecordAsync(callRecordId, ct);
        var ix = await ResolveAsync(record, interactionId, ct);
        var offer = await _offers.GetByIdAsync(addOfferId, ct)
            ?? throw new InvalidOperationException($"Offer {addOfferId} not found");

        var current = CartOf(record, ix);
        var removeSet = removeOfferIds.ToHashSet();
        var survivingItems = (current?.Items ?? []).Where(i => !removeSet.Contains(i.OfferId)).ToList();
        var newItem = BuildPricedItem(offer, quantity, survivingItems);

        var newCart = (current ?? CartDocument.Empty()) with { Items = [.. survivingItems, newItem] };
        return await ApplyAsync(record, ix, newCart, ct);
    }

    public async Task<CartOperationResult> RemoveOffersAsync(Guid callRecordId, IReadOnlyList<Guid> offerIds, CancellationToken ct = default, Guid? interactionId = null)
    {
        var record = await LoadRecordAsync(callRecordId, ct);
        var ix = await ResolveAsync(record, interactionId, ct);
        var current = CartOf(record, ix);
        var removeSet = offerIds.ToHashSet();
        var newItems = (current?.Items ?? []).Where(i => !removeSet.Contains(i.OfferId)).ToList();

        var newCart = (current ?? CartDocument.Empty()) with { Items = newItems };
        return await ApplyAsync(record, ix, newCart, ct);
    }

    public async Task<CartOperationResult> RemoveItemAsync(Guid callRecordId, int itemIndex, CancellationToken ct = default, Guid? interactionId = null)
    {
        var record = await LoadRecordAsync(callRecordId, ct);
        var ix = await ResolveAsync(record, interactionId, ct);
        var current = CartOf(record, ix);
        var items = current?.Items ?? [];
        if (itemIndex < 0 || itemIndex >= items.Count)
            throw new InvalidOperationException($"Item index {itemIndex} is out of range (cart has {items.Count} item(s)).");

        var newItems = items.Where((_, i) => i != itemIndex).ToList();
        var newCart = current! with { Items = newItems };
        return await ApplyAsync(record, ix, newCart, ct);
    }

    public async Task<CartOperationResult> UpdateQuantityAsync(Guid callRecordId, int itemIndex, int quantity, CancellationToken ct = default, Guid? interactionId = null)
    {
        if (quantity < 1) throw new InvalidOperationException("Quantity must be at least 1 — use RemoveItemAsync to remove an item.");

        var record = await LoadRecordAsync(callRecordId, ct);
        var ix = await ResolveAsync(record, interactionId, ct);
        var current = CartOf(record, ix);
        var items = current?.Items ?? [];
        if (itemIndex < 0 || itemIndex >= items.Count)
            throw new InvalidOperationException($"Item index {itemIndex} is out of range (cart has {items.Count} item(s)).");

        var target = items[itemIndex];
        var offer = await _offers.GetByIdAsync(target.OfferId, ct)
            ?? throw new InvalidOperationException($"Offer {target.OfferId} not found");

        var otherItems = items.Where((_, i) => i != itemIndex).ToList();
        var repriced = BuildPricedItem(offer, quantity, otherItems);

        var newItems = items.ToList();
        newItems[itemIndex] = repriced;
        var newCart = current! with { Items = newItems };
        return await ApplyAsync(record, ix, newCart, ct);
    }

    // ── Shared helpers ──────────────────────────────────────────────────────

    private async Task<CallRecord> LoadRecordAsync(Guid callRecordId, CancellationToken ct)
        => await _callRecords.GetByIdWithInteractionsAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found");

    /// <summary>The cart a change applies to: the interaction's (S178).</summary>
    private static CartDocument? CartOf(CallRecord record, CallInteraction? ix) => ix?.Cart;

    private static void Store(CallRecord record, CallInteraction? ix, CartDocument cart) => ix?.SetCart(cart);

    /// <summary>The interaction a cart change applies to. Every script session has one; a cart touched on a call with
    /// none yet (no script started) gets one, so the cart always has an owner.</summary>
    private async Task<CallInteraction> ResolveAsync(CallRecord record, Guid? interactionId, CancellationToken ct)
    {
        if (record.CommerceInteraction(interactionId) is { } existing) return existing;
        var created = record.AddInteraction(InteractionType.CustomerService);
        created.AssignTo(record.AgentId ?? Guid.Empty, record.CampaignId);
        await _callRecords.AddInteractionAsync(created, ct);
        return created;
    }

    private async Task<CartOperationResult> ApplyAsync(CallRecord record, CallInteraction? ix, CartDocument newCart, CancellationToken ct)
    {
        var current = CartOf(record, ix);
        await _inventory.ReleaseCartAsync(current, ct);

        var unavailable = await _inventory.ReserveCartAsync(newCart, ct);
        if (unavailable.Count > 0)
        {
            // Restore the old cart's reservations so the call record is left consistent.
            if (current is not null)
                await _inventory.ReserveCartAsync(current, ct);

            return CartOperationResult.Conflict(unavailable);
        }

        var calculated = await _pricing.CalculateTotalsAsync(newCart, await BuildTaxContextAsync(record, ix, ct), ct);
        Store(record, ix, calculated);
        await _callRecords.SaveChangesAsync(ct);

        return CartOperationResult.Success(calculated);
    }

    /// <summary>
    /// The call's tax context: its campaign's tax provider + settings (so every cart change is
    /// taxed by whatever the campaign is configured for) and the call record's addresses. A call
    /// with no campaign yet prices with the flat-rate default.
    /// </summary>
    private async Task<TaxContext> BuildTaxContextAsync(CallRecord record, CallInteraction? ix, CancellationToken ct)
    {
        // The interaction's campaign taxes its cart (S178: a CS order uses CS's tax provider).
        var campaignId = ix?.CampaignId ?? record.CampaignId;
        var campaign = campaignId == Guid.Empty ? null : await _campaigns.GetByIdAsync(campaignId, ct);
        return new TaxContext(
            CampaignId:   campaignId,
            ClientId:     record.ClientId,
            ProviderKey:  campaign?.TaxProvider ?? TaxProviderKey.FlatRate,
            SettingsJson: campaign?.TaxSettings,
            ShipTo:       record.Addresses?.Shipping,
            BillTo:       record.Addresses?.Billing);
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
            Sku: offer.EffectiveSku,
            Description: offer.Product.Description,
            Quantity: quantity,
            FullPrice: offer.FullPrice,
            ExtendedPrice: unitPrice * quantity,
            Shipping: offer.Shipping,
            Weight: offer.Product.Weight,
            SalesTax: 0,          // set by PricingService.CalculateTotalsAsync from the tax provider's per-line result
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
            ForeignSurcharge: 0,
            TaxCode: offer.EffectiveTaxCode);
    }
}
