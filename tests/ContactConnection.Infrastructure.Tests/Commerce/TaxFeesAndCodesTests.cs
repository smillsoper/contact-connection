using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using Moq;
using Xunit;
using static ContactConnection.Infrastructure.Tests.Commerce.TaxTestData;

namespace ContactConnection.Infrastructure.Tests.Commerce;

/// <summary>Per-item tax codes, and state fees (e.g. Colorado's Retail Delivery Fee) kept apart
/// from sales tax — for both Avalara and the flat-rate table.</summary>
public class TaxFeesAndCodesTests
{
    private static readonly DateOnly Date = new(2026, 9, 27);
    private static readonly AvalaraFeeLine CoRdf = new("CO", "OF400000", "Colorado Retail Delivery Fee", "CO_RDF");

    private static TaxContext AvalaraCtx() =>
        new(Guid.NewGuid(), Guid.NewGuid(), TaxProviderKey.Avalara, null, null, null);

    // ── Avalara: tax code precedence ─────────────────────────────────────────

    [Fact]
    public void Avalara_ItemTaxCode_BeatsCampaignDefault_ExemptStillNT()
    {
        var cart = Cart(Item("A", taxCode: "PC040100"), Item("B"), Item("C", taxExempt: true, taxCode: "PC040100"));
        var body = AvalaraTaxProvider.BuildRequest(new TaxRequest(cart, 0m, AvalaraCtx()),
            new AvalaraTaxSettings(ProductTaxCode: "PF050714"), Address(), Date);

        var lines = body["lines"]!.AsArray();
        Assert.Equal("PC040100", lines[0]!["taxCode"]!.GetValue<string>());
        Assert.Equal("PF050714", lines[1]!["taxCode"]!.GetValue<string>());
        Assert.Equal("NT", lines[2]!["taxCode"]!.GetValue<string>());
    }

    // ── Avalara: fee lines ───────────────────────────────────────────────────

    [Fact]
    public void Avalara_FeeLine_AddedOnlyWhenShipToStateMatches()
    {
        var settings = new AvalaraTaxSettings(FeeLines: [CoRdf]);

        var co = AvalaraTaxProvider.BuildRequest(new TaxRequest(Cart(Item()), 0m, AvalaraCtx()), settings, Address("80202", "co"), Date);
        var fee = co["lines"]!.AsArray().Last()!;
        Assert.Equal("fee-1", fee["number"]!.GetValue<string>());
        Assert.Equal("OF400000", fee["taxCode"]!.GetValue<string>());
        Assert.Equal(0m, fee["amount"]!.GetValue<decimal>());

        var or = AvalaraTaxProvider.BuildRequest(new TaxRequest(Cart(Item()), 0m, AvalaraCtx()), settings, Address(), Date);
        Assert.Single(or["lines"]!.AsArray());
    }

    [Fact]
    public void Avalara_FeeLineTax_BecomesFee_AndLeavesTheTaxTotal()
    {
        // Avalara's totalTax (3.19) includes the $0.29 fee returned as tax on the fee line.
        var json = JsonNode.Parse("""
            { "totalTax": 3.19,
              "lines": [ { "lineNumber": "1", "tax": 2.90 }, { "lineNumber": "fee-1", "tax": 0.29 } ],
              "summary": [] }
            """);

        var result = AvalaraTaxProvider.ParseResponse(json, 201, itemCount: 1, feeLines: [CoRdf]);

        Assert.Equal(2.90m, result.TaxAmount);
        var fee = Assert.Single(result.Fees!);
        Assert.Equal("CO_RDF", fee.Code);
        Assert.Equal("Colorado Retail Delivery Fee", fee.Description);
        Assert.Equal(0.29m, fee.Amount);
    }

    [Fact]
    public void Avalara_FeeNotApplied_ZeroFeeOmitted()
    {
        // e.g. nothing taxable in the order → Avalara returns 0 on the fee line.
        var json = JsonNode.Parse("""
            { "totalTax": 0, "lines": [ { "lineNumber": "1", "tax": 0 }, { "lineNumber": "fee-1", "tax": 0 } ] }
            """);
        var result = AvalaraTaxProvider.ParseResponse(json, 201, itemCount: 1, feeLines: [CoRdf]);
        Assert.Empty(result.Fees!);
    }

    [Fact]
    public void AvalaraFeeLine_DefaultCode()
        => Assert.Equal("MN_FEE", new AvalaraFeeLine("mn", "OF400000", "Minnesota Retail Delivery Fee").EffectiveCode);

    // ── Flat rate: per-state fee ─────────────────────────────────────────────

    private const string CoWithFee = """
        {"rates":[{"state":"CO","rate":0.029,"fee":{"description":"Colorado Retail Delivery Fee","amount":0.29,"code":"CO_RDF"}},
                  {"state":"MN","rate":0.06875,"fee":{"description":"Minnesota Retail Delivery Fee","amount":0.50,"minTaxableSubtotal":100}}]}
        """;

    private static Task<TaxResult> Flat(ContactConnection.Domain.ValueObjects.Commerce.CartDocument cart, string state)
        => new FlatRateTaxProvider().CalculateTaxAsync(new TaxRequest(cart, 0m,
            new TaxContext(Guid.NewGuid(), Guid.NewGuid(), TaxProviderKey.FlatRate, CoWithFee, Address("00000", state), null)));

    [Fact]
    public async Task Flat_StateFee_AppliedSeparatelyFromTax()
    {
        var result = await Flat(Cart(Item(price: 100m)), "CO");
        Assert.Equal(2.90m, result.TaxAmount);
        var fee = Assert.Single(result.Fees!);
        Assert.Equal(new ContactConnection.Domain.ValueObjects.Commerce.CartFee("CO_RDF", "Colorado Retail Delivery Fee", 0.29m), fee);
    }

    [Fact]
    public async Task Flat_StateFee_NotChargedWhenNothingTaxable()
        => Assert.Empty((await Flat(Cart(Item(price: 100m, taxExempt: true)), "CO")).Fees!);

    [Fact]
    public async Task Flat_StateFee_MinimumTaxableSubtotal_AndDefaultCode()
    {
        Assert.Empty((await Flat(Cart(Item(price: 99.99m)), "MN")).Fees!);
        var fee = Assert.Single((await Flat(Cart(Item(price: 100m)), "MN")).Fees!);
        Assert.Equal("MN_FEE", fee.Code);
        Assert.Equal(0.50m, fee.Amount);
    }

    [Fact]
    public void Flat_TaxRoundsHalfCentUp_NotBankers()
    {
        Assert.Equal(0.15m, FlatRateTaxProvider.RoundTax(0.145m));
        Assert.Equal(0.13m, FlatRateTaxProvider.RoundTax(0.125m));
    }

    // ── Pricing: fees are in the total, not in SalesTax ──────────────────────

    [Fact]
    public async Task Pricing_FeesIncludedInTotal_NotInSalesTax()
    {
        var pricing = new PricingService(new TaxProviderFactory([new FlatRateTaxProvider()]));
        var ctx = new TaxContext(Guid.NewGuid(), Guid.NewGuid(), TaxProviderKey.FlatRate, CoWithFee, Address("80202", "CO"), null);

        var priced = await pricing.CalculateTotalsAsync(Cart(Item(price: 100m)), ctx);

        Assert.Equal(2.90m, priced.SalesTax);
        Assert.Equal(0.29m, Assert.Single(priced.Fees!).Amount);
        Assert.Equal(100m + 2.90m + 0.29m, priced.CartTotal);
        Assert.Equal(priced.CartTotal, priced.PaymentBreakdowns.Sum(b => b.Total));
    }
}
