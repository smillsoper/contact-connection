using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;
using ContactConnection.Infrastructure.Credentials;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Commerce;

/// <summary>Campaign-level Avalara settings (Campaign.TaxSettings JSON). Credentials are not here —
/// see <see cref="AvalaraTaxProvider"/>.</summary>
public record AvalaraTaxSettings(
    string? CompanyCode = null,
    string? ProductTaxCode = null,
    string? ShippingTaxCode = null,
    string? CustomerCode = null,
    AddressData? ShipFrom = null,
    List<AvalaraFeeLine>? FeeLines = null);

/// <summary>
/// A state fee Avalara calculates from a dedicated line — e.g. Colorado's Retail Delivery Fee:
/// State "CO", TaxCode "OF400000". When the ship-to state matches, the provider adds a line with
/// this tax code (amount <see cref="Amount"/>, normally 0); Avalara decides whether the fee applies
/// (it doesn't when nothing in the order is taxable) and returns it as that line's "tax", which the
/// provider reports as a CartFee instead of tax. <see cref="Code"/> identifies the fee downstream
/// (defaults to "{State}_FEE").
/// </summary>
public record AvalaraFeeLine(string State, string TaxCode, string Description, string? Code = null, decimal Amount = 0)
{
    public string EffectiveCode => string.IsNullOrWhiteSpace(Code) ? $"{State.ToUpperInvariant()}_FEE" : Code;
}

/// <summary>
/// Avalara AvaTax sales tax (POST /api/v2/transactions/create, type SalesOrder, commit=false —
/// a quote only; nothing is recorded in Avalara). Request layout mirrors the CRMPro integration
/// Life Seasons used (docs/integrations/life-seasons-crmpro-legacy.md): one line per cart item,
/// then a shipping line, then a negative DISCOUNT line, then any configured fee lines for the
/// ship-to state (see AvalaraFeeLine) — whose "tax" comes back as a CartFee, not sales tax.
/// Item tax code: "NT" for tax-exempt offers, else the item's own code (offer → product), else the
/// campaign's ProductTaxCode.
///
/// Credentials (tenant credential store, campaign -> client -> tenant cascade — see
/// ScopedCredentials): Avalara:{scope}:AccountId, Avalara:{scope}:LicenseKey, and optional
/// Avalara:{scope}:Environment ("production"; anything else = sandbox).
///
/// Never throws for expected failures — returns a zero-tax result with status pending_address /
/// error and a message the agent sees on the cart, so a tax problem never blocks editing the cart.
/// </summary>
public class AvalaraTaxProvider(
    IHttpClientFactory httpClientFactory,
    ITenantCredentialStore credentials,
    ILogger<AvalaraTaxProvider> logger) : ITaxProvider
{
    public string ProviderKey => TaxProviderKey.Avalara;

    internal const string SandboxUrl    = "https://sandbox-rest.avatax.com/api/v2/transactions/create";
    internal const string ProductionUrl = "https://rest.avatax.com/api/v2/transactions/create";
    internal const string ShippingLineNumber = "shipping";
    internal const string DiscountLineNumber = "discount";

    private static readonly JsonSerializerOptions SettingsJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<TaxResult> CalculateTaxAsync(TaxRequest request, CancellationToken ct = default)
    {
        var context = request.Context;
        if (request.Cart.Items.Count == 0)
            return new TaxResult(0, 0, LineTaxes: []);

        var shipTo = context?.ShipTo ?? context?.BillTo;
        if (context is null || shipTo is null || string.IsNullOrWhiteSpace(shipTo.Zip))
            return TaxResult.Zero(TaxCalculationStatus.PendingAddress,
                "Sales tax will be calculated once a shipping address is captured.");

        AvalaraTaxSettings settings;
        try
        {
            settings = string.IsNullOrWhiteSpace(context.SettingsJson)
                ? new AvalaraTaxSettings()
                : JsonSerializer.Deserialize<AvalaraTaxSettings>(context.SettingsJson, SettingsJsonOptions) ?? new AvalaraTaxSettings();
        }
        catch (JsonException)
        {
            return TaxResult.Zero(TaxCalculationStatus.Error, "This campaign's Avalara tax settings are invalid.");
        }

        var accountId  = await ScopedCredentials.ResolveAsync(credentials, "Avalara", "AccountId", context.CampaignId, context.ClientId, ct);
        var licenseKey = await ScopedCredentials.ResolveAsync(credentials, "Avalara", "LicenseKey", context.CampaignId, context.ClientId, ct);
        if (accountId is null || licenseKey is null)
            return TaxResult.Zero(TaxCalculationStatus.Error,
                "Avalara credentials are not configured for this campaign (Avalara AccountId / LicenseKey).");
        var environment = await ScopedCredentials.ResolveAsync(credentials, "Avalara", "Environment", context.CampaignId, context.ClientId, ct);
        var url = string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase) ? ProductionUrl : SandboxUrl;

        var body = BuildRequest(request, settings, shipTo, DateOnly.FromDateTime(DateTime.UtcNow));

        JsonNode? json;
        int status;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{accountId}:{licenseKey}")));
            message.Headers.TryAddWithoutValidation("X-Avalara-Client",
                $"ContactConnection; 1.0; ContactConnection.AvalaraTaxProvider; 1.0; {Environment.MachineName}");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await httpClientFactory.CreateClient("Avalara").SendAsync(message, timeout.Token);
            status = (int)response.StatusCode;
            var raw = await response.Content.ReadAsStringAsync(timeout.Token);
            json = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Avalara tax request failed for campaign {CampaignId}.", context.CampaignId);
            return TaxResult.Zero(TaxCalculationStatus.Error, "Could not reach Avalara to calculate sales tax.");
        }

        return ParseResponse(json, status, request.Cart.Items.Count, AppliedFeeLines(settings, shipTo));
    }

    /// <summary>The campaign's fee lines that apply to this ship-to state, in request order.</summary>
    internal static List<AvalaraFeeLine> AppliedFeeLines(AvalaraTaxSettings settings, AddressData shipTo)
        => (settings.FeeLines ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.TaxCode)
                        && string.Equals(f.State, shipTo.State?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

    internal static string FeeLineNumber(int index) => $"fee-{index + 1}";

    /// <summary>Builds the CreateTransactionModel body. Internal for tests.</summary>
    internal static JsonObject BuildRequest(TaxRequest request, AvalaraTaxSettings settings, AddressData shipTo, DateOnly date)
    {
        var cart = request.Cart;
        var lines = new JsonArray();
        for (var i = 0; i < cart.Items.Count; i++)
        {
            var item = cart.Items[i];
            var line = new JsonObject
            {
                ["number"]   = (i + 1).ToString(CultureInfo.InvariantCulture),
                ["quantity"] = item.Quantity,
                ["amount"]   = item.ExtendedPrice + item.PersonalizationAnswers.Sum(a => a.ChargeAmount) * item.Quantity,
                ["itemCode"] = item.Sku,
                ["description"] = item.Description,
            };
            // "NT" is Avalara's standard non-taxable code — an offer marked tax-exempt stays exempt
            // regardless of any tax code. Otherwise the item's own code wins over the campaign default.
            var taxCode = item.TaxExempt ? "NT"
                : !string.IsNullOrWhiteSpace(item.TaxCode) ? item.TaxCode
                : settings.ProductTaxCode;
            if (!string.IsNullOrWhiteSpace(taxCode)) line["taxCode"] = taxCode;
            lines.Add(line);
        }

        if (request.Shipping > 0)
        {
            var shipping = new JsonObject
            {
                ["number"]      = ShippingLineNumber,
                ["quantity"]    = 1,
                ["amount"]      = request.Shipping,
                ["itemCode"]    = string.IsNullOrWhiteSpace(cart.ShipMethod) ? "SHIPPING" : cart.ShipMethod,
                ["description"] = "Shipping",
            };
            if (!string.IsNullOrWhiteSpace(settings.ShippingTaxCode)) shipping["taxCode"] = settings.ShippingTaxCode;
            lines.Add(shipping);
        }

        if (cart.Discount > 0)
        {
            lines.Add(new JsonObject
            {
                ["number"]      = DiscountLineNumber,
                ["quantity"]    = 1,
                ["amount"]      = -cart.Discount,
                ["itemCode"]    = "DISCOUNT",
                ["description"] = "Discount",
            });
        }

        var feeLines = AppliedFeeLines(settings, shipTo);
        for (var i = 0; i < feeLines.Count; i++)
        {
            lines.Add(new JsonObject
            {
                ["number"]      = FeeLineNumber(i),
                ["quantity"]    = 1,
                ["amount"]      = feeLines[i].Amount,
                ["taxCode"]     = feeLines[i].TaxCode,
                ["itemCode"]    = feeLines[i].EffectiveCode,
                ["description"] = feeLines[i].Description,
            });
        }

        // With a ship-from address, send shipFrom + shipTo; without one, Avalara requires a single
        // location, so the ship-to address stands in for both.
        var addresses = new JsonObject();
        if (settings.ShipFrom is { } shipFrom && !string.IsNullOrWhiteSpace(shipFrom.Zip))
        {
            addresses["shipFrom"] = ToLocation(shipFrom);
            addresses["shipTo"]   = ToLocation(shipTo);
        }
        else
        {
            addresses["singleLocation"] = ToLocation(shipTo);
        }

        var body = new JsonObject
        {
            ["lines"]        = lines,
            ["type"]         = "SalesOrder",
            ["date"]         = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["customerCode"] = string.IsNullOrWhiteSpace(settings.CustomerCode) ? "ContactConnection" : settings.CustomerCode,
            ["addresses"]    = addresses,
            ["commit"]       = false,
            ["currencyCode"] = "USD",
            ["description"]  = "Phone order",
        };
        if (!string.IsNullOrWhiteSpace(settings.CompanyCode)) body["companyCode"] = settings.CompanyCode;
        return body;
    }

    internal static JsonObject ToLocation(AddressData a)
    {
        var line1 = string.Join(" ", new[] { a.Prefix, a.Street }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var line2 = string.Join(" ", new[] { a.UnitPrefix, a.Unit }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var country = a.IsCanada ? "CA" : string.IsNullOrWhiteSpace(a.Country) ? "US" : a.Country;
        var location = new JsonObject();
        if (line1.Length > 0) location["line1"] = line1;
        if (line2.Length > 0) location["line2"] = line2;
        if (!string.IsNullOrWhiteSpace(a.City)) location["city"] = a.City;
        if (!string.IsNullOrWhiteSpace(a.State)) location["region"] = a.State;
        location["country"] = country;
        location["postalCode"] = a.Zip;
        return location;
    }

    /// <summary>Maps an AvaTax response (or error) to a TaxResult. Internal for tests.
    /// Per-item tax is matched by line number (1..itemCount), not by item code, so two cart lines
    /// with the same SKU still get their own tax. Fee lines' "tax" becomes CartFees and is taken back
    /// out of the tax total (Avalara's totalTax includes it).</summary>
    internal static TaxResult ParseResponse(
        JsonNode? json, int httpStatus, int itemCount, IReadOnlyList<AvalaraFeeLine>? feeLines = null)
    {
        var error = json?["error"];
        if (error is not null || httpStatus is < 200 or >= 300)
        {
            var message = error?["message"]?.GetValue<string>();
            var detail  = error?["details"]?.AsArray().FirstOrDefault()?["message"]?.GetValue<string>();
            var text = detail ?? message ?? $"Avalara returned HTTP {httpStatus}.";
            return TaxResult.Zero(TaxCalculationStatus.Error, $"Avalara could not calculate sales tax: {text}");
        }

        var totalTax = Dec(json?["totalTax"]) ?? 0m;
        var lineTaxes = new decimal[itemCount];
        decimal shippingTax = 0;
        var fees = new List<CartFee>();
        feeLines ??= [];
        foreach (var line in json?["lines"]?.AsArray() ?? [])
        {
            var number = line?["lineNumber"]?.GetValue<string>();
            var tax = Dec(line?["tax"]) ?? Dec(line?["taxCalculated"]) ?? 0m;
            if (number == ShippingLineNumber)
                shippingTax = tax;
            else if (number is not null && number.StartsWith("fee-", StringComparison.Ordinal)
                     && int.TryParse(number[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var f)
                     && f >= 1 && f <= feeLines.Count)
            {
                if (tax != 0) fees.Add(new CartFee(feeLines[f - 1].EffectiveCode, feeLines[f - 1].Description, tax));
            }
            else if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= itemCount)
                lineTaxes[n - 1] = tax;
        }
        totalTax -= fees.Sum(x => x.Amount);

        var summary = json?["summary"]?.AsArray() ?? [];
        var rate = summary.Sum(s => Dec(s?["rate"]) ?? 0m);
        var jurisdictions = summary
            .Where(s => s is not null)
            .Select(s => new JsonNodeJurisdiction(s!).ToJurisdictionTax())
            .ToList();

        return new TaxResult(rate, totalTax, jurisdictions, lineTaxes, shippingTax, TaxCalculationStatus.Calculated, Fees: fees);
    }

    private static decimal? Dec(JsonNode? node)
        => node is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;

    private readonly record struct JsonNodeJurisdiction(JsonNode Node)
    {
        public JurisdictionTax ToJurisdictionTax() => new(
            Jurisdiction: Node["jurisName"]?.GetValue<string>() ?? "",
            TaxType: (Node["jurisType"]?.GetValue<string>() ?? "").ToLowerInvariant() switch
            {
                "sta" or "state"  => "state",
                "cty" or "county" => "county",
                "cit" or "city"   => "city",
                _                 => "special",
            },
            Rate: Dec(Node["rate"]) ?? 0m,
            Amount: Dec(Node["tax"]) ?? 0m);
    }
}
