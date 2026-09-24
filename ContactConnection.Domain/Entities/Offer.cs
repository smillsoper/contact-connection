using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// A sales configuration for a Product — the "how it's sold" layer.
///
/// One Product can have multiple Offers for different campaigns, channels, or price points:
///   - "TV Special"     → $29.95 / 3 payments
///   - "Web Offer"      → $24.95 single pay
///   - "Retention Offer"→ $19.95 / 2 payments, supervisor-only
///
/// This separation keeps inventory tracking accurate: all Offers for a Product
/// decrement the same QtyAvailable, regardless of which Offer was used at order time.
///
/// CartItem snapshots OfferId + ProductId + Sku at order time, so changing an Offer
/// after an order is placed does not retroactively alter existing cart items.
/// </summary>
public class Offer
{
    // Identity
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }

    // Scope — null/null = tenant-wide (the default, matching every offer created before this
    // existed). A campaign-scoped offer must also carry its client. Mirrors CustomFieldDefinition's
    // scope model, minus its "most specific wins" collapse — offers are meant to coexist (TV
    // Special, Web Offer, Retention Offer can all be simultaneously valid), not resolve to one.
    public Guid? ClientId { get; private set; }
    public Guid? CampaignId { get; private set; }

    // Display
    public string Name { get; private set; } = "";  // e.g. "TV Special", "Web Offer", "Upsell"

    // Pricing
    public decimal FullPrice { get; private set; }
    public bool AllowPriceOverride { get; private set; }

    // Shipping per offer (e.g. "free shipping" offer vs standard)
    public decimal Shipping { get; private set; }
    public bool TaxExempt { get; private set; }
    public bool ShippingExempt { get; private set; }

    // Mix & match — groups items across cart for cross-item price breaks
    public string? MixMatchCode { get; private set; }

    // Upsell
    public bool IsUpsell { get; private set; }
    public int UpsellQty { get; private set; }
    public int UpsellQtyOfEntry { get; private set; }   // qty of the entry item that triggers this upsell
    public decimal UpsellCommission { get; private set; }
    public decimal UpsellClientAmount { get; private set; }

    // AutoShip
    public bool AutoShip { get; private set; }
    public bool AutoShipOptional { get; private set; }

    // Ship-to / delivery options
    public bool AllowShipTo { get; private set; }
    public bool ShipToRequired { get; private set; }
    public bool AllowDeliveryMessage { get; private set; }
    public bool ShipMethodPerItem { get; private set; }

    // Availability
    public bool IsActive { get; private set; }
    public DateTimeOffset? ValidFrom { get; private set; }  // null = no start restriction
    public DateTimeOffset? ValidTo { get; private set; }    // null = no end restriction

    // JSONB — complex pricing and configuration structures
    public List<PaymentInstallment> Payments { get; private set; } = [];
    public List<QuantityPriceBreak> QuantityPriceBreaks { get; private set; } = [];
    public List<QuantityPriceBreak> MixMatchPriceBreaks { get; private set; } = [];
    public List<AutoShipInterval> AutoShipIntervals { get; private set; } = [];
    public List<ProductShipMethod> ShipMethods { get; private set; } = [];
    public List<PersonalizationPrompt> Personalization { get; private set; } = [];
    public List<ProductFlag> Flags { get; private set; } = [];

    // Audit
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    public Product Product { get; private set; } = null!;

    // Required by EF Core
    private Offer() { }

    public static Offer Create(
        Guid tenantId,
        Guid productId,
        string name,
        decimal fullPrice,
        decimal shipping = 0)
    {
        var now = DateTimeOffset.UtcNow;
        return new Offer
        {
            Id        = Guid.NewGuid(),
            TenantId  = tenantId,
            ProductId = productId,
            Name      = name,
            FullPrice = fullPrice,
            Shipping  = shipping,
            IsActive  = false,      // drafts start inactive; must be explicitly activated
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Activate()
    {
        IsActive  = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive  = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCampaignWindow(DateTimeOffset? validFrom, DateTimeOffset? validTo)
    {
        ValidFrom = validFrom;
        ValidTo   = validTo;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetPricing(
        decimal fullPrice,
        List<PaymentInstallment> payments,
        List<QuantityPriceBreak>? quantityPriceBreaks = null,
        List<QuantityPriceBreak>? mixMatchPriceBreaks = null,
        bool allowPriceOverride = false)
    {
        FullPrice           = fullPrice;
        Payments            = payments;
        QuantityPriceBreaks = quantityPriceBreaks ?? [];
        MixMatchPriceBreaks = mixMatchPriceBreaks ?? [];
        AllowPriceOverride  = allowPriceOverride;
        UpdatedAt           = DateTimeOffset.UtcNow;
    }

    public void SetShipping(
        decimal shipping,
        bool shippingExempt = false,
        bool taxExempt = false,
        bool shipMethodPerItem = false,
        bool allowShipTo = false,
        bool shipToRequired = false,
        bool allowDeliveryMessage = false,
        List<ProductShipMethod>? shipMethods = null)
    {
        Shipping             = shipping;
        ShippingExempt       = shippingExempt;
        TaxExempt            = taxExempt;
        ShipMethodPerItem    = shipMethodPerItem;
        AllowShipTo          = allowShipTo;
        ShipToRequired       = shipToRequired;
        AllowDeliveryMessage = allowDeliveryMessage;
        ShipMethods          = shipMethods ?? [];
        UpdatedAt            = DateTimeOffset.UtcNow;
    }

    public void SetAutoShip(bool autoShip, bool optional, List<AutoShipInterval> intervals)
    {
        AutoShip          = autoShip;
        AutoShipOptional  = optional;
        AutoShipIntervals = intervals;
        UpdatedAt         = DateTimeOffset.UtcNow;
    }

    public void Rename(string name)
    {
        Name      = name;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetScope(Guid? clientId, Guid? campaignId)
    {
        if (campaignId.HasValue && !clientId.HasValue)
            throw new ArgumentException("A campaign-scoped offer must also specify its client.", nameof(clientId));

        ClientId   = clientId;
        CampaignId = campaignId;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    public void SetMixMatch(string? code, List<QuantityPriceBreak>? priceBreaks = null)
    {
        MixMatchCode        = code;
        MixMatchPriceBreaks = priceBreaks ?? [];
        UpdatedAt           = DateTimeOffset.UtcNow;
    }

    public void SetUpsell(
        bool isUpsell, int upsellQty, int upsellQtyOfEntry,
        decimal commission, decimal clientAmount)
    {
        IsUpsell           = isUpsell;
        UpsellQty          = upsellQty;
        UpsellQtyOfEntry   = upsellQtyOfEntry;
        UpsellCommission   = commission;
        UpsellClientAmount = clientAmount;
        UpdatedAt          = DateTimeOffset.UtcNow;
    }

    public void SetPersonalization(List<PersonalizationPrompt> prompts)
    {
        Personalization = prompts;
        UpdatedAt       = DateTimeOffset.UtcNow;
    }

    public void SetFlags(List<ProductFlag> flags)
    {
        Flags     = flags;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Whether this offer is currently available to add to a cart.
    /// Checks IsActive and optional campaign validity window.
    /// </summary>
    public bool IsAvailable()
    {
        if (!IsActive) return false;
        var now = DateTimeOffset.UtcNow;
        if (ValidFrom.HasValue && now < ValidFrom.Value) return false;
        if (ValidTo.HasValue   && now > ValidTo.Value)   return false;
        return true;
    }
}
