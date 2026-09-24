namespace ContactConnection.Domain.Entities;

/// <summary>
/// The physical item definition in a tenant's catalog.
/// A Product is what you stock and ship — SKU, weight, inventory.
///
/// A Product can have one or more Offers: the sales configurations that define
/// how it is priced and sold in a given campaign. The same Product with different
/// Offers allows a TV spot, a web ad, and an upsell to each carry independent
/// pricing, payment plans, and personalization without duplicating inventory tracking.
/// </summary>
public class Product
{
    // Identity
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    // Core catalog
    public string Sku { get; private set; } = "";
    public string Description { get; private set; } = "";
    public bool Searchable { get; private set; } = true;
    public bool ReportingOnly { get; private set; }

    // Variant — null for base products; set for size/color/etc. variants
    public Guid? ParentProductId { get; private set; }

    // Physical — used for shipping weight calculations
    public decimal Weight { get; private set; }

    // Geographic surcharges (flat amounts added per item in affected zip codes)
    // These are physical item characteristics — the same regardless of which Offer sells the item.
    public decimal CanadaSurcharge { get; private set; }
    public decimal AKHISurcharge { get; private set; }
    public decimal OutlyingUSSurcharge { get; private set; }
    public decimal ForeignSurcharge { get; private set; }

    // Inventory
    public ProductInventoryStatus InventoryStatus { get; private set; } = ProductInventoryStatus.Available;
    public bool DecrementOnOrder { get; private set; }
    public int QtyAvailable { get; private set; }
    public int QtyReserved { get; private set; }      // units held in active carts, not yet committed
    public int MinimumQty { get; private set; }
    public int QtyLimit { get; private set; }           // 0 = no limit
    public int QtyLimitException { get; private set; }
    public int ExpectedQuantity { get; private set; }
    public DateOnly? ExpectedStockDate { get; private set; }
    public string? BackorderMessage { get; private set; }
    public string? DiscontinuedMessage { get; private set; }

    // Search / discovery
    public List<string> AliasSKUs { get; private set; } = [];
    public List<string> Keywords { get; private set; } = [];

    // Audit
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    private readonly List<ProductKit> _kits = [];
    public IReadOnlyList<ProductKit> Kits => _kits.AsReadOnly();

    private readonly List<Offer> _offers = [];
    public IReadOnlyList<Offer> Offers => _offers.AsReadOnly();

    private readonly List<ProductCategory> _categories = [];
    public IReadOnlyList<ProductCategory> Categories => _categories.AsReadOnly();

    private readonly List<ProductAttributeValue> _attributeValues = [];
    public IReadOnlyList<ProductAttributeValue> AttributeValues => _attributeValues.AsReadOnly();

    // Required by EF Core
    private Product() { }

    public static Product Create(
        Guid tenantId,
        string sku,
        string description,
        decimal weight = 0)
    {
        var now = DateTimeOffset.UtcNow;
        return new Product
        {
            Id          = Guid.NewGuid(),
            TenantId    = tenantId,
            Sku         = sku,
            Description = description,
            Weight      = weight,
            Searchable  = true,
            CreatedAt   = now,
            UpdatedAt   = now
        };
    }

    public void SetPhysical(decimal weight)
    {
        Weight    = weight;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetGeographicSurcharges(
        decimal canada = 0, decimal akhi = 0,
        decimal outlyingUs = 0, decimal foreign = 0)
    {
        CanadaSurcharge     = canada;
        AKHISurcharge       = akhi;
        OutlyingUSSurcharge = outlyingUs;
        ForeignSurcharge    = foreign;
        UpdatedAt           = DateTimeOffset.UtcNow;
    }

    public void SetInventory(
        ProductInventoryStatus status,
        int qtyAvailable,
        bool decrementOnOrder = true,
        int minimumQty = 0,
        int qtyLimit = 0,
        int qtyLimitException = 0,
        int expectedQuantity = 0,
        DateOnly? expectedStockDate = null,
        string? backorderMessage = null,
        string? discontinuedMessage = null)
    {
        InventoryStatus     = status;
        QtyAvailable        = qtyAvailable;
        DecrementOnOrder    = decrementOnOrder;
        MinimumQty          = minimumQty;
        QtyLimit            = qtyLimit;
        QtyLimitException   = qtyLimitException;
        ExpectedQuantity    = expectedQuantity;
        ExpectedStockDate   = expectedStockDate;
        BackorderMessage    = backorderMessage;
        DiscontinuedMessage = discontinuedMessage;
        UpdatedAt           = DateTimeOffset.UtcNow;
    }

    public void SetCatalog(bool searchable, bool reportingOnly, List<string>? keywords = null, List<string>? aliasSKUs = null)
    {
        Searchable    = searchable;
        ReportingOnly = reportingOnly;
        Keywords      = keywords ?? [];
        AliasSKUs     = aliasSKUs ?? [];
        UpdatedAt     = DateTimeOffset.UtcNow;
    }

    public void SetVariant(Guid parentProductId)
    {
        ParentProductId = parentProductId;
        UpdatedAt       = DateTimeOffset.UtcNow;
    }

    public void AssignToCategory(ProductCategory category)
    {
        if (!_categories.Any(c => c.Id == category.Id))
            _categories.Add(category);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RemoveFromCategory(Guid categoryId)
    {
        _categories.RemoveAll(c => c.Id == categoryId);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetAttributeValue(ProductAttributeValue value)
    {
        // One value per attribute — remove any existing value for same attribute
        _attributeValues.RemoveAll(v => v.AttributeId == value.AttributeId);
        _attributeValues.Add(value);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RemoveAttributeValue(Guid attributeValueId)
    {
        _attributeValues.RemoveAll(v => v.Id == attributeValueId);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void DecrementInventory(int qty)
    {
        if (DecrementOnOrder)
            QtyAvailable -= qty;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Whether the given quantity can be added to a cart.
    /// Discontinued and OutOfStock products are never orderable. NoBackorder products require
    /// sufficient stock net of already-reserved units.
    /// </summary>
    public bool CanAddToCart(int qty) =>
        InventoryStatus switch
        {
            ProductInventoryStatus.Discontinued => false,
            ProductInventoryStatus.OutOfStock    => false,
            ProductInventoryStatus.NoBackorder   => QtyAvailable - QtyReserved - qty >= MinimumQty,
            _                                    => true
        };

    /// <summary>
    /// Soft-reserves <paramref name="qty"/> units for an active cart.
    /// Returns false (and makes no change) if stock is insufficient for NoBackorder products.
    /// </summary>
    public bool Reserve(int qty)
    {
        if (!CanAddToCart(qty))
            return false;

        if (DecrementOnOrder)
            QtyReserved += qty;

        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Releases a previously soft-reserved quantity (cart cleared or session abandoned).
    /// </summary>
    public void Release(int qty)
    {
        if (DecrementOnOrder)
            QtyReserved = Math.Max(0, QtyReserved - qty);

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Confirms the reservation on order commit: converts reserved units into a real
    /// inventory decrement and clears the reservation.
    /// </summary>
    public void Confirm(int qty)
    {
        if (DecrementOnOrder)
        {
            QtyAvailable = Math.Max(0, QtyAvailable - qty);
            QtyReserved  = Math.Max(0, QtyReserved  - qty);
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum ProductInventoryStatus
{
    Available    = 0,
    CanBackorder = 1,
    NoBackorder  = 2,
    Discontinued = 3,
    // Temporarily unavailable (expected to return) — distinct from Discontinued (gone for good).
    // Same CanAddToCart behavior as Discontinued today; kept separate for reporting/messaging.
    OutOfStock   = 4
}
