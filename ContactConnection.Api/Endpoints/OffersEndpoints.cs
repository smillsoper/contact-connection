using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Api.Endpoints;

public static class OffersEndpoints
{
    public static IEndpointRouteBuilder MapOffersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/offers").RequireAuthorization();

        group.MapPost("", Create);
        group.MapGet("", GetActive);
        group.MapGet("{id:guid}", GetById);
        group.MapPut("{id:guid}", Update);
        group.MapPost("{id:guid}/activate", Activate);
        group.MapPost("{id:guid}/deactivate", Deactivate);
        group.MapGet("product/{productId:guid}", GetByProduct);

        return app;
    }

    // ── POST /api/v1/offers ──────────────────────────────────────────────────

    private static async Task<IResult> Create(
        CreateOfferRequest req,
        IOfferRepository offers,
        IProductRepository products,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var product = await products.GetByIdAsync(req.ProductId, ct);
        if (product is null)
            return Results.NotFound(new { error = $"Product {req.ProductId} not found." });

        var offer = Offer.Create(
            tenantContext.Current!.Id,
            req.ProductId,
            req.Name,
            req.FullPrice,
            req.Shipping);

        // No payment schedule given (the common case for a simple admin-created offer) — default
        // to a single full-payment installment so the offer actually prices at FullPrice instead of
        // $0. PricingService sums Payments, never FullPrice, to compute what a cart item charges.
        var payments = req.Payments is { Count: > 0 }
            ? req.Payments
            : [new PaymentInstallment(1, "Full payment", req.FullPrice, 0)];
        offer.SetPricing(
            req.FullPrice,
            payments,
            req.QuantityPriceBreaks,
            req.MixMatchPriceBreaks,
            req.AllowPriceOverride);

        if (req.ClientId.HasValue)
        {
            try { offer.SetScope(req.ClientId, req.CampaignIds); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }

        if (req.MixMatchCode is not null)
            offer.SetMixMatch(req.MixMatchCode, req.MixMatchPriceBreaks);

        if (!string.IsNullOrWhiteSpace(req.TaxCode))
            offer.SetTaxCode(req.TaxCode);

        offer.SetSku(req.Sku);
        if (req.Flags is { Count: > 0 })
            offer.SetFlags(CleanFlags(req.Flags));

        if (req.IsUpsell)
            offer.SetUpsell(
                true,
                req.UpsellQty,
                req.UpsellQtyOfEntry,
                req.UpsellCommission,
                req.UpsellClientAmount);

        if (req.AutoShip)
            offer.SetAutoShip(true, req.AutoShipOptional, req.AutoShipIntervals ?? []);

        if (req.Personalization is { Count: > 0 })
            offer.SetPersonalization(req.Personalization);

        if (req.ValidFrom.HasValue || req.ValidTo.HasValue)
            offer.SetCampaignWindow(req.ValidFrom, req.ValidTo);

        offer.SetShipping(
            req.Shipping,
            req.ShippingExempt,
            req.TaxExempt,
            req.ShipMethodPerItem,
            req.AllowShipTo,
            req.ShipToRequired,
            req.AllowDeliveryMessage,
            req.ShipMethods);

        await offers.AddAsync(offer, ct);
        await offers.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/offers/{offer.Id}", ToResponse(offer));
    }

    // ── GET /api/v1/offers ───────────────────────────────────────────────────

    private static async Task<IResult> GetActive(
        IOfferRepository offers,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var list = await offers.GetActiveAsync(ct);
        return Results.Ok(list.Select(ToResponse));
    }

    // ── GET /api/v1/offers/{id} ──────────────────────────────────────────────

    private static async Task<IResult> GetById(
        Guid id,
        IOfferRepository offers,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var offer = await offers.GetByIdAsync(id, ct);
        return offer is null ? Results.NotFound() : Results.Ok(ToResponse(offer));
    }

    // ── PUT /api/v1/offers/{id} ──────────────────────────────────────────────
    // Covers the fields the admin Offer editor exposes: name, price/shipping, tax/shipping
    // exemption, scope, and (S169, the fields Life Seasons' offers use) SKU override, AutoShip,
    // upsell and flags. Those newer fields are optional on the request — omitted = left unchanged,
    // so an older client can't wipe them. QPB, MixMatch, personalization, ship-to options and the
    // campaign window stay API-only until a feature needs them.

    private static async Task<IResult> Update(
        Guid id,
        UpdateOfferRequest req,
        IOfferRepository offers,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var offer = await offers.GetByIdAsync(id, ct);
        if (offer is null) return Results.NotFound();

        offer.Rename(req.Name);

        // A real multi-payment plan (2+ installments) was configured through the full API and
        // isn't something this simple form understands — leave it untouched rather than silently
        // collapsing it back to a single payment. 0 or 1 installments (the common case for an
        // offer created through this same admin UI) gets kept in sync with the edited price.
        if (offer.Payments.Count <= 1)
            offer.SetPricing(req.FullPrice, [new PaymentInstallment(1, "Full payment", req.FullPrice, 0)], offer.QuantityPriceBreaks, offer.MixMatchPriceBreaks, offer.AllowPriceOverride);

        offer.SetShipping(
            req.Shipping,
            req.ShippingExempt,
            req.TaxExempt,
            offer.ShipMethodPerItem,
            offer.AllowShipTo,
            offer.ShipToRequired,
            offer.AllowDeliveryMessage,
            offer.ShipMethods);

        try { offer.SetScope(req.ClientId, req.CampaignIds); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        offer.SetTaxCode(req.TaxCode);

        if (req.Sku is not null) offer.SetSku(req.Sku);
        if (req.AutoShip is not null)
            offer.SetAutoShip(req.AutoShip.Value, req.AutoShipOptional ?? offer.AutoShipOptional,
                req.AutoShipIntervals?.Where(i => i.IntervalDays > 0).ToList() ?? offer.AutoShipIntervals);
        if (req.Upsell is not null)
            offer.SetUpsell(req.Upsell.IsUpsell, req.Upsell.UpsellQty, req.Upsell.UpsellQtyOfEntry,
                req.Upsell.UpsellCommission, req.Upsell.UpsellClientAmount);
        if (req.Flags is not null) offer.SetFlags(CleanFlags(req.Flags));

        await offers.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(offer));
    }

    // ── POST /api/v1/offers/{id}/activate ────────────────────────────────────

    private static async Task<IResult> Activate(
        Guid id,
        IOfferRepository offers,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var offer = await offers.GetByIdAsync(id, ct);
        if (offer is null) return Results.NotFound();

        offer.Activate();
        await offers.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(offer));
    }

    // ── POST /api/v1/offers/{id}/deactivate ──────────────────────────────────

    private static async Task<IResult> Deactivate(
        Guid id,
        IOfferRepository offers,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var offer = await offers.GetByIdAsync(id, ct);
        if (offer is null) return Results.NotFound();

        offer.Deactivate();
        await offers.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(offer));
    }

    // ── GET /api/v1/offers/product/{productId} ───────────────────────────────

    private static async Task<IResult> GetByProduct(
        Guid productId,
        IOfferRepository offers,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var list = await offers.GetByProductIdAsync(productId, ct);
        return Results.Ok(list.Select(ToResponse));
    }

    /// <summary>Drops blank flag names and trims — the editor sends whatever rows are on screen.</summary>
    private static List<ProductFlag> CleanFlags(IEnumerable<ProductFlag> flags)
        => flags.Where(f => !string.IsNullOrWhiteSpace(f.Name))
                .Select(f => new ProductFlag(f.Name.Trim(), f.Value?.Trim() ?? ""))
                .ToList();

    // ── Response shape ───────────────────────────────────────────────────────

    internal static object ToResponse(Offer o) => new
    {
        o.Id,
        o.ProductId,
        o.Name,
        o.Sku,
        EffectiveSku = o.Product is null ? o.Sku : o.EffectiveSku,
        o.ClientId,
        o.CampaignIds,
        o.FullPrice,
        o.Shipping,
        o.TaxExempt,
        o.ShippingExempt,
        o.TaxCode,
        o.AllowPriceOverride,
        o.IsActive,
        o.ValidFrom,
        o.ValidTo,
        o.MixMatchCode,
        Upsell = new
        {
            o.IsUpsell,
            o.UpsellQty,
            o.UpsellQtyOfEntry,
            o.UpsellCommission,
            o.UpsellClientAmount
        },
        AutoShip = new
        {
            o.AutoShip,
            o.AutoShipOptional,
            o.AutoShipIntervals
        },
        ShipOptions = new
        {
            o.ShipMethodPerItem,
            o.AllowShipTo,
            o.ShipToRequired,
            o.AllowDeliveryMessage,
            o.ShipMethods
        },
        Pricing = new
        {
            o.Payments,
            o.QuantityPriceBreaks,
            o.MixMatchPriceBreaks
        },
        o.Personalization,
        o.Flags,
        o.CreatedAt,
        o.UpdatedAt
    };
}

// ── Request records ──────────────────────────────────────────────────────────

public record CreateOfferRequest(
    Guid ProductId,
    string Name,
    decimal FullPrice,
    decimal Shipping = 0,
    bool TaxExempt = false,
    bool ShippingExempt = false,
    bool AllowPriceOverride = false,
    Guid? ClientId = null,
    List<Guid>? CampaignIds = null,
    string? MixMatchCode = null,
    bool IsUpsell = false,
    int UpsellQty = 0,
    int UpsellQtyOfEntry = 0,
    decimal UpsellCommission = 0,
    decimal UpsellClientAmount = 0,
    bool AutoShip = false,
    bool AutoShipOptional = false,
    bool ShipMethodPerItem = false,
    bool AllowShipTo = false,
    bool ShipToRequired = false,
    bool AllowDeliveryMessage = false,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    List<PaymentInstallment>? Payments = null,
    List<QuantityPriceBreak>? QuantityPriceBreaks = null,
    List<QuantityPriceBreak>? MixMatchPriceBreaks = null,
    List<AutoShipInterval>? AutoShipIntervals = null,
    List<ProductShipMethod>? ShipMethods = null,
    List<PersonalizationPrompt>? Personalization = null,
    string? TaxCode = null,
    string? Sku = null,
    List<ProductFlag>? Flags = null);

public record UpdateOfferRequest(
    string Name,
    decimal FullPrice,
    decimal Shipping,
    bool TaxExempt,
    bool ShippingExempt,
    Guid? ClientId,
    List<Guid>? CampaignIds,
    string? TaxCode = null,
    string? Sku = null,
    bool? AutoShip = null,
    bool? AutoShipOptional = null,
    List<AutoShipInterval>? AutoShipIntervals = null,
    OfferUpsellRequest? Upsell = null,
    List<ProductFlag>? Flags = null);

public record OfferUpsellRequest(
    bool IsUpsell,
    int UpsellQty = 0,
    int UpsellQtyOfEntry = 0,
    decimal UpsellCommission = 0,
    decimal UpsellClientAmount = 0);
