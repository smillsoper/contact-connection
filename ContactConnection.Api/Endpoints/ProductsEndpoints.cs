using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class ProductsEndpoints
{
    public static IEndpointRouteBuilder MapProductsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products").RequireAuthorization();

        group.MapPost("", Create);
        group.MapGet("", Search);
        group.MapGet("{id:guid}", GetById);
        group.MapPut("{id:guid}", Update);
        group.MapGet("sku/{sku}", GetBySku);

        return app;
    }

    // ── POST /api/v1/products ────────────────────────────────────────────────

    private static async Task<IResult> Create(
        CreateProductRequest req,
        IProductRepository products,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var existing = await products.GetBySkuAsync(req.Sku, ct);
        if (existing is not null)
            return Results.Conflict(new { error = $"SKU '{req.Sku}' already exists." });

        var product = Product.Create(
            tenantContext.Current!.Id,
            req.Sku,
            req.Description,
            req.Weight);

        if (req.InventoryStatus.HasValue)
            product.SetInventory(
                req.InventoryStatus.Value,
                req.QtyAvailable ?? 0,
                req.DecrementOnOrder ?? true);

        if (req.GeographicSurcharges is not null)
            product.SetGeographicSurcharges(
                req.GeographicSurcharges.Canada,
                req.GeographicSurcharges.AKHI,
                req.GeographicSurcharges.OutlyingUS,
                req.GeographicSurcharges.Foreign);

        await products.AddAsync(product, ct);
        await products.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/products/{product.Id}", ToResponse(product));
    }

    // ── GET /api/v1/products ─────────────────────────────────────────────────

    private static async Task<IResult> Search(
        IProductRepository products,
        TenantContext tenantContext,
        CancellationToken ct,
        string? query = null, Guid? categoryId = null, Guid[]? attributeValueIds = null,
        int page = 1, int pageSize = 20, bool includeAll = false)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var list = await products.SearchAsync(query, categoryId, attributeValueIds, page, pageSize, includeAll, ct);
        return Results.Ok(list.Select(ToResponse));
    }

    // ── GET /api/v1/products/{id} ────────────────────────────────────────────

    private static async Task<IResult> GetById(
        Guid id,
        IProductRepository products,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var product = await products.GetByIdAsync(id, ct);
        return product is null ? Results.NotFound() : Results.Ok(ToResponse(product));
    }

    // ── PUT /api/v1/products/{id} ────────────────────────────────────────────
    // Sku/Description are immutable by design (Product has no setter for either) — shown
    // read-only in the admin editor rather than adding new mutation surface for this.

    private static async Task<IResult> Update(
        Guid id,
        UpdateProductRequest req,
        IProductRepository products,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var product = await products.GetByIdAsync(id, ct);
        if (product is null) return Results.NotFound();

        product.SetPhysical(req.Weight);
        product.SetInventory(req.InventoryStatus, req.QtyAvailable, req.DecrementOnOrder, req.MinimumQty);
        product.SetCatalog(req.Searchable, product.ReportingOnly, product.Keywords, product.AliasSKUs);

        await products.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(product));
    }

    // ── GET /api/v1/products/sku/{sku} ───────────────────────────────────────

    private static async Task<IResult> GetBySku(
        string sku,
        IProductRepository products,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var product = await products.GetBySkuAsync(sku, ct);
        return product is null ? Results.NotFound() : Results.Ok(ToResponse(product));
    }

    // ── Response shape ───────────────────────────────────────────────────────

    internal static object ToResponse(Product p) => new
    {
        p.Id,
        p.Sku,
        p.Description,
        p.Weight,
        p.Searchable,
        p.ReportingOnly,
        p.ParentProductId,
        Inventory = new
        {
            p.InventoryStatus,
            p.QtyAvailable,
            p.DecrementOnOrder,
            p.MinimumQty,
            p.QtyLimit,
            p.ExpectedStockDate,
            p.BackorderMessage,
            p.DiscontinuedMessage
        },
        Surcharges = new
        {
            p.CanadaSurcharge,
            p.AKHISurcharge,
            p.OutlyingUSSurcharge,
            p.ForeignSurcharge
        },
        p.Keywords,
        p.AliasSKUs,
        Kits = p.Kits.Select(k => new
        {
            k.Id,
            k.IsVariable,
            k.Qty,
            k.ChildProductId,
            k.KitPrompt,
            k.MultiSelect,
            k.ChoiceSkus
        }),
        Offers = p.Offers.Select(OffersEndpoints.ToResponse),
        Categories = p.Categories.Select(c => new { c.Id, c.Name, c.Slug, c.ParentId }),
        AttributeValues = p.AttributeValues.Select(v => new
        {
            v.Id,
            v.AttributeId,
            v.Value,
            v.DisplayOrder
        }),
        p.CreatedAt,
        p.UpdatedAt
    };
}

// ── Request records ──────────────────────────────────────────────────────────

public record GeographicSurchargeRequest(
    decimal Canada = 0,
    decimal AKHI = 0,
    decimal OutlyingUS = 0,
    decimal Foreign = 0);

public record CreateProductRequest(
    string Sku,
    string Description,
    decimal Weight = 0,
    ProductInventoryStatus? InventoryStatus = null,
    int? QtyAvailable = null,
    bool? DecrementOnOrder = null,
    GeographicSurchargeRequest? GeographicSurcharges = null);

public record UpdateProductRequest(
    decimal Weight,
    ProductInventoryStatus InventoryStatus,
    int QtyAvailable,
    bool DecrementOnOrder,
    int MinimumQty,
    bool Searchable);
