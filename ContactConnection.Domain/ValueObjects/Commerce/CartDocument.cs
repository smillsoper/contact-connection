namespace ContactConnection.Domain.ValueObjects.Commerce;

/// <summary>
/// A range-keyed value used for shipping tier lookups.
/// For weight tiers: if total cart weight >= RangeMin (and no higher tier qualifies), shipping cost = Value.
/// For subtotal tiers: same lookup against cart subtotal.
/// Resolution: highest qualifying RangeMin wins.
/// </summary>
public record TierRange(decimal RangeMin, decimal Value);

/// <summary>
/// Per-installment breakdown of cart totals, produced by PricingService.CalculateTotals
/// when the product has a multi-payment schedule.
/// </summary>
public record CartPaymentBreakdown(
    int PaymentNumber,
    decimal Subtotal,
    decimal Shipping,
    decimal SalesTax,
    decimal Discount,
    decimal PersonalizationCharge,
    decimal Total);

/// <summary>An agent's answer to a personalization prompt on a cart item.</summary>
public record CartPersonalizationAnswer(
    string Name,
    string Answer,
    decimal ChargeAmount);

/// <summary>A variable kit selection made by the agent.</summary>
public record CartKitSelection(
    Guid KitId,
    string SelectedSku,
    int Qty);

/// <summary>
/// A line item in the cart. All prices are a snapshot of the offer at the time the
/// item was added — changing an Offer or Product after an order is placed does not
/// retroactively alter the cart item. Payments holds the resolved installment schedule
/// after QPB and MixMatch price break evaluation.
/// </summary>
public record CartItem(
    Guid OfferId,       // which Offer was used (TV Special, Web Offer, etc.)
    Guid ProductId,     // snapshot — the physical item
    string Sku,         // snapshot — the physical item's SKU
    string Description,
    int Quantity,
    decimal FullPrice,
    decimal ExtendedPrice,          // FullPrice × Quantity (after price break resolution)
    decimal Shipping,
    decimal Weight,
    decimal SalesTax,
    bool ShippingExempt,
    bool TaxExempt,
    bool OnBackOrder,
    bool AutoShip,
    int AutoShipIntervalDays,
    bool IsUpsell,
    int UpsellQty,
    string? MixMatchCode,
    string? ShipMethod,             // per-item override; null = use cart-level ShipMethod
    string? DeliveryMessage,
    string? ShipToJson,             // serialized AddressData; null = ship to billing address
    List<PaymentInstallment> Payments,
    List<CartPersonalizationAnswer> PersonalizationAnswers,
    List<CartKitSelection> KitSelections,
    decimal CanadaSurcharge,
    decimal AKHISurcharge,
    decimal OutlyingUSSurcharge,
    decimal ForeignSurcharge,
    // Snapshot of the offer's effective tax code (offer override → product) at add time; null =
    // the campaign's default product tax code. Defaulted so older stored carts still deserialize.
    string? TaxCode = null);

/// <summary>
/// A non-tax charge computed alongside tax — e.g. Colorado's Retail Delivery Fee. Kept apart from
/// SalesTax because such fees must be stated separately on invoices and order APIs report them
/// separately; still included in CartTotal. <see cref="Code"/> identifies the fee to downstream
/// systems (e.g. "CO_RDF"); <see cref="Description"/> is what the agent and invoices show.
/// </summary>
public record CartFee(string Code, string Description, decimal Amount);

/// <summary>
/// The full cart for a call record, stored as a JSONB document on call_records.cart.
/// Totals are computed by PricingService.CalculateTotalsAsync and stored alongside the items
/// so reads never need to recalculate.
/// </summary>
public record CartDocument(
    List<CartItem> Items,
    string? ShippingZip,
    string? ShipMethod,
    decimal Discount,
    decimal TaxRate,
    /// <summary>
    /// Selects the tax calculation provider.
    /// null / "" = FlatRateTaxProvider (uses TaxRate directly).
    /// Future values: "avalara", "taxjar".
    /// </summary>
    string? TaxProvider,
    bool SplitShippingInPayments,
    bool SplitSalesTaxInPayments,
    List<TierRange> ShippingWeightTiers,
    List<TierRange> ShippingSubtotalTiers,
    // Computed by PricingService — do not set manually
    decimal CartSubtotal,
    decimal Shipping,
    decimal SalesTax,
    decimal PersonalizationCharge,
    decimal CartTotal,
    List<CartPaymentBreakdown> PaymentBreakdowns,
    // Computed by PricingService alongside SalesTax. ShippingTax is the portion of SalesTax charged
    // on shipping (order APIs often report it separately). TaxStatus is a TaxCalculationStatus
    // value; TaxMessage explains a non-"calculated" status for the agent. Defaulted so carts
    // stored before these existed still deserialize.
    decimal ShippingTax = 0,
    string? TaxStatus = null,
    string? TaxMessage = null,
    // Fees computed by the tax provider (see CartFee) — included in CartTotal, not in SalesTax.
    List<CartFee>? Fees = null)
{
    /// <summary>An empty cart ready for item addition.</summary>
    public static CartDocument Empty() => new(
        Items: [],
        ShippingZip: null,
        ShipMethod: null,
        Discount: 0,
        TaxRate: 0,
        TaxProvider: null,
        SplitShippingInPayments: false,
        SplitSalesTaxInPayments: false,
        ShippingWeightTiers: [],
        ShippingSubtotalTiers: [],
        CartSubtotal: 0,
        Shipping: 0,
        SalesTax: 0,
        PersonalizationCharge: 0,
        CartTotal: 0,
        PaymentBreakdowns: []);
}
