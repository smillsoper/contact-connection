using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Infrastructure.Commerce;

/// <summary>One taxable state and its rate (a fraction: 0.029 = 2.9%). <see cref="TaxShipping"/>
/// taxes the shipping charge at the same rate — states that tax shipping use the goods rate, not a
/// separate one — prorated to the taxable share of the cart (see FlatRateTaxProvider).</summary>
public record StateTaxRate(string State, decimal Rate, bool TaxShipping = false, StateFee? Fee = null);

/// <summary>
/// A fixed per-order state fee, e.g. Colorado's Retail Delivery Fee or Minnesota's (which only
/// applies to larger orders — <see cref="MinTaxableSubtotal"/>). Charged when the order has at least
/// one taxable item and the taxable subtotal meets the minimum; reported as a CartFee, not tax.
/// Static amounts go stale (Colorado adjusts its fee every July).
/// </summary>
public record StateFee(string Description, decimal Amount, decimal MinTaxableSubtotal = 0, string? Code = null);

/// <summary>Campaign-level flat-rate settings (Campaign.TaxSettings JSON):
/// {"rates":[{"state":"CO","rate":0.029,"taxShipping":false}, ...]}. States not listed are not taxed.</summary>
public record FlatRateTaxSettings(List<StateTaxRate>? Rates = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static FlatRateTaxSettings Parse(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new FlatRateTaxSettings()
            : JsonSerializer.Deserialize<FlatRateTaxSettings>(json, JsonOptions) ?? new FlatRateTaxSettings();
}

/// <summary>
/// Default tax provider — the "loose" option for clients with no tax API but a known list of
/// taxable states: a static percentage per state, applied to the taxable subtotal (items not
/// marked TaxExempt, personalization charges included). Shipping is taxed only for states flagged
/// TaxShipping, and then only in proportion to the taxable share of the cart (the common rule for
/// mixed taxable/exempt shipments — equal to taxing all of it when every item is taxable). The state is the
/// call's ship-to address (billing as fallback); states not in the campaign's list are not taxed.
/// No I/O. Static rates can go stale — a platform-run tax API is the intended long-term
/// replacement.
///
/// With no TaxContext (a cart priced outside any call) it falls back to CartDocument.TaxRate.
///
/// The total is rounded once, then split across taxable lines proportionally, with any rounding
/// remainder on the last taxable line — so per-item tax always sums exactly to the total.
/// </summary>
public class FlatRateTaxProvider : ITaxProvider
{
    public string ProviderKey => TaxProviderKey.FlatRate;

    /// <summary>Sales tax rounds half a cent UP (0.145 → 0.15). Math.Round's default is banker's
    /// rounding (0.145 → 0.14), which under-collects tax on exact half-cent amounts.</summary>
    internal static decimal RoundTax(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public Task<TaxResult> CalculateTaxAsync(TaxRequest request, CancellationToken ct = default)
    {
        var cart = request.Cart;
        decimal rate;
        var taxShipping = false;
        StateFee? fee = null;
        if (request.Context is null)
        {
            rate = cart.TaxRate;
        }
        else
        {
            FlatRateTaxSettings settings;
            try { settings = FlatRateTaxSettings.Parse(request.Context.SettingsJson); }
            catch (JsonException)
            {
                return Task.FromResult(TaxResult.Zero(TaxCalculationStatus.Error, "This campaign's tax rate settings are invalid."));
            }

            var rates = settings.Rates ?? [];
            if (rates.Count == 0)
                return Task.FromResult(new TaxResult(0, 0, LineTaxes: new decimal[cart.Items.Count]));

            var state = (request.Context.ShipTo ?? request.Context.BillTo)?.State?.Trim();
            if (string.IsNullOrEmpty(state))
                return Task.FromResult(TaxResult.Zero(TaxCalculationStatus.PendingAddress,
                    "Sales tax will be calculated once a shipping address is captured."));

            var match = rates.FirstOrDefault(r => string.Equals(r.State, state, StringComparison.OrdinalIgnoreCase));
            rate = match?.Rate ?? 0m;
            taxShipping = match?.TaxShipping ?? false;
            fee = match?.Fee;
        }

        var lineBases = cart.Items
            .Select(i => i.TaxExempt ? 0m : i.ExtendedPrice + i.PersonalizationAnswers.Sum(a => a.ChargeAmount) * i.Quantity)
            .ToList();
        var taxableSubtotal = lineBases.Sum();
        var goodsTax = RoundTax(taxableSubtotal * rate);

        decimal shippingTax = 0;
        if (taxShipping && request.Shipping > 0 && taxableSubtotal > 0)
        {
            var fullSubtotal = cart.Items.Sum(i => i.ExtendedPrice + i.PersonalizationAnswers.Sum(a => a.ChargeAmount) * i.Quantity);
            var taxableShipping = fullSubtotal > 0 ? request.Shipping * taxableSubtotal / fullSubtotal : request.Shipping;
            shippingTax = RoundTax(taxableShipping * rate);
        }

        var lineTaxes = new decimal[lineBases.Count];
        var taxAmount = goodsTax;
        if (taxAmount != 0 && taxableSubtotal != 0)
        {
            var lastTaxable = lineBases.FindLastIndex(b => b != 0);
            decimal allocated = 0;
            for (var i = 0; i < lineBases.Count; i++)
            {
                if (lineBases[i] == 0) continue;
                lineTaxes[i] = i == lastTaxable
                    ? taxAmount - allocated
                    : RoundTax(taxAmount * lineBases[i] / taxableSubtotal);
                allocated += lineTaxes[i];
            }
        }

        List<CartFee> fees = [];
        if (fee is { Amount: > 0 } && taxableSubtotal > 0 && taxableSubtotal >= fee.MinTaxableSubtotal)
        {
            var state = (request.Context?.ShipTo ?? request.Context?.BillTo)?.State?.Trim().ToUpperInvariant();
            fees.Add(new CartFee(string.IsNullOrWhiteSpace(fee.Code) ? $"{state}_FEE" : fee.Code, fee.Description, fee.Amount));
        }

        // TaxAmount is the full tax (goods + shipping); LineTaxes cover the goods only; fees are separate.
        return Task.FromResult(new TaxResult(
            Rate: rate, TaxAmount: goodsTax + shippingTax, LineTaxes: lineTaxes, ShippingTax: shippingTax, Fees: fees));
    }
}
