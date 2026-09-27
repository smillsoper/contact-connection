using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Calculates sales tax for a cart. The active provider is chosen per campaign
/// (Campaign.TaxProvider — see TaxProviderKey): "" = FlatRateTaxProvider, "avalara" =
/// AvalaraTaxProvider. New providers are registered in DI and dispatched by ITaxProviderFactory;
/// adding one never requires changes to PricingService.
/// </summary>
public interface ITaxProvider
{
    /// <summary>The provider identifier that selects this implementation (e.g. "avalara").</summary>
    string ProviderKey { get; }

    /// <summary>
    /// Calculate tax for the given cart. Does not mutate the cart. Implementations should not
    /// throw for expected failures (missing address, bad credentials, vendor error) — return a
    /// TaxResult with a non-"calculated" Status and a Message instead, so a tax problem never
    /// blocks the agent from editing the cart.
    /// </summary>
    Task<TaxResult> CalculateTaxAsync(TaxRequest request, CancellationToken ct = default);
}

/// <summary>
/// Everything a provider may need. <see cref="Shipping"/> is the cart's shipping total as
/// PricingService computed it (tiers applied) — providers that tax shipping need it, and it isn't
/// on the cart document yet at this point in the calculation.
/// </summary>
public record TaxRequest(CartDocument Cart, decimal Shipping, TaxContext? Context);

/// <summary>
/// Call-level context for tax: which campaign/client (credential scope), the provider's campaign
/// settings JSON (Campaign.TaxSettings), and the addresses on the call record. Null when pricing a
/// cart outside any call (e.g. an API preview) — providers then fall back to cart-only data.
/// </summary>
public record TaxContext(
    Guid CampaignId,
    Guid ClientId,
    string ProviderKey,
    string? SettingsJson,
    AddressData? ShipTo,
    AddressData? BillTo);

/// <summary>
/// The result of a tax calculation. <see cref="TaxAmount"/> is the full tax, shipping tax
/// included, fees excluded. <see cref="LineTaxes"/>, when a provider returns it, has exactly one
/// entry per cart item in cart order — PricingService stores each on its CartItem.SalesTax.
/// <see cref="Fees"/> are non-tax charges (e.g. Colorado's Retail Delivery Fee) the cart states
/// separately but still includes in its total.
/// </summary>
public record TaxResult(
    decimal Rate,
    decimal TaxAmount,
    List<JurisdictionTax>? Jurisdictions = null,
    IReadOnlyList<decimal>? LineTaxes = null,
    decimal ShippingTax = 0,
    string Status = "calculated",
    string? Message = null,
    IReadOnlyList<CartFee>? Fees = null)
{
    public static TaxResult Zero(string status, string? message) => new(0, 0, Status: status, Message: message);
}

/// <summary>Per-jurisdiction tax line for multi-state nexus display.</summary>
public record JurisdictionTax(
    string Jurisdiction,
    string TaxType,         // "state" | "county" | "city" | "special"
    decimal Rate,
    decimal Amount);
