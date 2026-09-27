using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Commerce;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static ContactConnection.Infrastructure.Tests.Commerce.TaxTestData;

namespace ContactConnection.Infrastructure.Tests.Commerce;

public class AvalaraTaxProviderTests
{
    private static readonly DateOnly Date = new(2026, 9, 27);

    private static TaxContext Ctx(AddressData? shipTo = null, AddressData? billTo = null, string? settings = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), TaxProviderKey.Avalara, settings, shipTo, billTo);

    // ── Request ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildRequest_ItemsShippingDiscount_InLegacyLayout()
    {
        var cart = Cart(Item("283-1-CTY-P10-SN", 49.95m), Item("GIFT", 5m, taxExempt: true)) with
        {
            ShipMethod = "REG", Discount = 5m,
        };
        var settings = new AvalaraTaxSettings(ProductTaxCode: "PF050714", ShippingTaxCode: "FR020200");

        var body = AvalaraTaxProvider.BuildRequest(new TaxRequest(cart, 6.95m, Ctx()), settings, Address(), Date);

        var lines = body["lines"]!.AsArray();
        Assert.Equal(4, lines.Count);
        Assert.Equal("1", lines[0]!["number"]!.GetValue<string>());
        Assert.Equal("PF050714", lines[0]!["taxCode"]!.GetValue<string>());
        Assert.Equal(49.95m, lines[0]!["amount"]!.GetValue<decimal>());
        Assert.Equal("NT", lines[1]!["taxCode"]!.GetValue<string>()); // tax-exempt offer stays exempt
        Assert.Equal("shipping", lines[2]!["number"]!.GetValue<string>());
        Assert.Equal("FR020200", lines[2]!["taxCode"]!.GetValue<string>());
        Assert.Equal("REG", lines[2]!["itemCode"]!.GetValue<string>());
        Assert.Equal(6.95m, lines[2]!["amount"]!.GetValue<decimal>());
        Assert.Equal("discount", lines[3]!["number"]!.GetValue<string>());
        Assert.Equal(-5m, lines[3]!["amount"]!.GetValue<decimal>());

        Assert.Equal("SalesOrder", body["type"]!.GetValue<string>());
        Assert.False(body["commit"]!.GetValue<bool>());
        Assert.Equal("2026-09-27", body["date"]!.GetValue<string>());
        Assert.Null(body["companyCode"]); // omitted → Avalara's default company
    }

    [Fact]
    public void BuildRequest_NoShipping_NoDiscount_OmitsThoseLines()
    {
        var body = AvalaraTaxProvider.BuildRequest(
            new TaxRequest(Cart(Item()), 0m, Ctx()), new AvalaraTaxSettings(), Address(), Date);

        var lines = body["lines"]!.AsArray();
        Assert.Single(lines);
        Assert.Null(lines[0]!["taxCode"]); // no campaign tax code → Avalara default
    }

    [Fact]
    public void BuildRequest_WithShipFrom_SendsShipFromAndShipTo_ElseSingleLocation()
    {
        var shipFrom = new AddressData { Street = "565 N Kays Dr.", City = "Kaysville", State = "UT", Zip = "84037" };
        var withFrom = AvalaraTaxProvider.BuildRequest(new TaxRequest(Cart(Item()), 0m, Ctx()),
            new AvalaraTaxSettings(CompanyCode: "LIFESEASONS", ShipFrom: shipFrom), Address(), Date);
        Assert.Equal("84037", withFrom["addresses"]!["shipFrom"]!["postalCode"]!.GetValue<string>());
        Assert.Equal("97470", withFrom["addresses"]!["shipTo"]!["postalCode"]!.GetValue<string>());
        Assert.Equal("LIFESEASONS", withFrom["companyCode"]!.GetValue<string>());

        var without = AvalaraTaxProvider.BuildRequest(new TaxRequest(Cart(Item()), 0m, Ctx()),
            new AvalaraTaxSettings(), Address(), Date);
        Assert.NotNull(without["addresses"]!["singleLocation"]);
        Assert.Null(without["addresses"]!["shipTo"]);
    }

    [Fact]
    public void ToLocation_JoinsPrefixAndUnit_AndMapsCanada()
    {
        var loc = AvalaraTaxProvider.ToLocation(new AddressData
        {
            Prefix = "PO Box", Street = "123", UnitPrefix = "Apt", Unit = "4",
            City = "Toronto", State = "ON", Zip = "M5V2T6", IsCanada = true,
        });
        Assert.Equal("PO Box 123", loc["line1"]!.GetValue<string>());
        Assert.Equal("Apt 4", loc["line2"]!.GetValue<string>());
        Assert.Equal("CA", loc["country"]!.GetValue<string>());
        Assert.Equal("ON", loc["region"]!.GetValue<string>());
    }

    // ── Response ─────────────────────────────────────────────────────────────

    [Fact]
    public void ParseResponse_MapsLineTaxByNumber_AndShippingTax()
    {
        var json = JsonNode.Parse("""
            {
              "totalTax": 4.12,
              "lines": [
                { "lineNumber": "2", "tax": 0.50 },
                { "lineNumber": "1", "tax": 3.12 },
                { "lineNumber": "shipping", "tax": 0.50 }
              ],
              "summary": [
                { "jurisType": "STA", "jurisName": "COLORADO", "rate": 0.029, "tax": 1.50 },
                { "jurisType": "CIT", "jurisName": "DENVER", "rate": 0.0481, "tax": 2.62 }
              ]
            }
            """);

        var result = AvalaraTaxProvider.ParseResponse(json, 201, itemCount: 2);

        Assert.Equal(TaxCalculationStatus.Calculated, result.Status);
        Assert.Equal(4.12m, result.TaxAmount);
        Assert.Equal([3.12m, 0.50m], result.LineTaxes!);
        Assert.Equal(0.50m, result.ShippingTax);
        Assert.Equal(0.0771m, result.Rate);
        Assert.Equal("state", result.Jurisdictions![0].TaxType);
        Assert.Equal("city", result.Jurisdictions![1].TaxType);
    }

    [Fact]
    public void ParseResponse_ErrorBody_IsErrorStatusWithDetailMessage()
    {
        var json = JsonNode.Parse("""
            { "error": { "code": "AddressRangeError", "message": "Address not found.",
              "details": [ { "message": "The address value was incomplete." } ] } }
            """);

        var result = AvalaraTaxProvider.ParseResponse(json, 400, itemCount: 1);

        Assert.Equal(TaxCalculationStatus.Error, result.Status);
        Assert.Equal(0m, result.TaxAmount);
        Assert.Contains("The address value was incomplete.", result.Message);
    }

    [Fact]
    public void ParseResponse_NonSuccessWithoutBody_IsError()
    {
        var result = AvalaraTaxProvider.ParseResponse(null, 401, itemCount: 1);
        Assert.Equal(TaxCalculationStatus.Error, result.Status);
        Assert.Contains("401", result.Message);
    }

    // ── Guards before any HTTP call ──────────────────────────────────────────

    private static AvalaraTaxProvider Provider(Mock<ITenantCredentialStore> creds)
        => new(new Mock<IHttpClientFactory>(MockBehavior.Strict).Object, creds.Object,
               NullLogger<AvalaraTaxProvider>.Instance);

    [Fact]
    public async Task NoAddress_IsPendingAddress_WithoutCallingAvalara()
    {
        var creds = new Mock<ITenantCredentialStore>(MockBehavior.Strict);
        var result = await Provider(creds).CalculateTaxAsync(new TaxRequest(Cart(Item()), 0m, Ctx()));
        Assert.Equal(TaxCalculationStatus.PendingAddress, result.Status);
    }

    [Fact]
    public async Task BillingAddressOnly_IsUsedAsFallback()
    {
        // Billing present but no credentials → gets past the address check to the credential check.
        var creds = new Mock<ITenantCredentialStore>();
        var result = await Provider(creds).CalculateTaxAsync(
            new TaxRequest(Cart(Item()), 0m, Ctx(billTo: Address())));
        Assert.Equal(TaxCalculationStatus.Error, result.Status);
        Assert.Contains("credentials", result.Message);
    }

    [Fact]
    public async Task MissingCredentials_IsError_NotAThrow()
    {
        var creds = new Mock<ITenantCredentialStore>();
        var result = await Provider(creds).CalculateTaxAsync(
            new TaxRequest(Cart(Item()), 0m, Ctx(shipTo: Address())));
        Assert.Equal(TaxCalculationStatus.Error, result.Status);
        Assert.Equal(0m, result.TaxAmount);
    }

    [Fact]
    public async Task InvalidSettingsJson_IsError()
    {
        var creds = new Mock<ITenantCredentialStore>(MockBehavior.Strict);
        var result = await Provider(creds).CalculateTaxAsync(
            new TaxRequest(Cart(Item()), 0m, Ctx(shipTo: Address(), settings: "{\"shipFrom\": 5}")));
        Assert.Equal(TaxCalculationStatus.Error, result.Status);
    }
}
