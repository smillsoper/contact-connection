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

    // Scope — no client = tenant-wide (the default, matching every offer created before this
    // existed). CampaignIds narrows a client's offer to some of its campaigns (S169: e.g. NeuroQ offers
    // on the NeuroQ scripts but not My Best Heart, same client); empty = all of the client's campaigns.
    // Offers are meant to coexist (TV Special, Web Offer, Retention Offer all valid at once), not
    // resolve to one — no "most specific wins".
    public Guid? ClientId { get; private set; }
    public List<Guid> CampaignIds { get; private set; } = [];

    // Display
    public string Name { get; private set; } = "";  // e.g. "TV Special", "Web Offer", "Upsell"

    /// <summary>SKU override (S169) — fulfillment houses often give each variant of a product its own
    /// SKU (Life Seasons: "283-3-CTY-90" vs "283-3-OT-1" for the same item). Null = the product's SKU.
    /// The cart line, order line, order API and tax request all carry <see cref="EffectiveSku"/>.</summary>
    public string? Sku { get; private set; }

    // Pricing
    public decimal FullPrice { get; private set; }
    public bool AllowPriceOverride { get; private set; }

    // Shipping per offer (e.g. "free shipping" offer vs standard)
    public decimal Shipping { get; private set; }
    public bool TaxExempt { get; private set; }
    public bool ShippingExempt { get; private set; }

    /// <summary>Overrides the product's tax code for this offer (e.g. a bundle taxed differently).
    /// Null = use Product.TaxCode, then the campaign default. See <see cref="EffectiveTaxCode"/>.</summary>
    public string? TaxCode { get; private set; }

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

    public void SetScope(Guid? clientId, IEnumerable<Guid>? campaignIds = null)
    {
        var campaigns = (campaignIds ?? []).Where(c => c != Guid.Empty).Distinct().ToList();
        if (campaigns.Count > 0 && !clientId.HasValue)
            throw new ArgumentException("A campaign-scoped offer must also specify its client.", nameof(clientId));

        ClientId    = clientId;
        CampaignIds = campaigns;
        UpdatedAt   = DateTimeOffset.UtcNow;
    }

    /// <summary>Tenant-wide, or on this client and (no campaign list, or this campaign listed).</summary>
    public bool AvailableFor(Guid? clientId, Guid? campaignId)
        => (ClientId is null || ClientId == clientId)
        && (CampaignIds.Count == 0 || (campaignId.HasValue && CampaignIds.Contains(campaignId.Value)));

    public void SetSku(string? sku)
    {
        Sku       = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Offer SKU override, else the product's SKU (Product must be loaded).</summary>
    public string EffectiveSku => Sku ?? Product?.Sku ?? "";

    public void SetTaxCode(string? taxCode)
    {
        TaxCode   = string.IsNullOrWhiteSpace(taxCode) ? null : taxCode.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Offer override, else the product's own code (Product must be loaded), else null.</summary>
    public string? EffectiveTaxCode => TaxCode ?? Product?.TaxCode;

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
