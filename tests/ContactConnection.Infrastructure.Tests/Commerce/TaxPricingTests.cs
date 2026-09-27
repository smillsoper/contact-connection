using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using Moq;
using Xunit;
using static ContactConnection.Infrastructure.Tests.Commerce.TaxTestData;

namespace ContactConnection.Infrastructure.Tests.Commerce;

/// <summary>Flat-rate provider allocation + how PricingService applies any provider's result.</summary>
public class TaxPricingTests
{
    private const string CoUtRates = """{"rates":[{"state":"CO","rate":0.029},{"state":"UT","rate":0.0485}]}""";

    private static TaxContext FlatCtx(string? settings, ContactConnection.Domain.ValueObjects.AddressData? shipTo = null,
        ContactConnection.Domain.ValueObjects.AddressData? billTo = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), TaxProviderKey.FlatRate, settings, shipTo, billTo);

    private static Task<TaxResult> Flat(ContactConnection.Domain.ValueObjects.Commerce.CartDocument cart, TaxContext? ctx, decimal shipping = 0m)
        => new FlatRateTaxProvider().CalculateTaxAsync(new TaxRequest(cart, shipping, ctx));

    [Fact]
    public async Task FlatRate_StateWithoutShippingFlag_DoesNotTaxShipping()
    {
        var result = await Flat(Cart(Item(price: 100m)), FlatCtx(CoUtRates, shipTo: Address("80202", "CO")), shipping: 10m);
        Assert.Equal(2.90m, result.TaxAmount);
        Assert.Equal(0m, result.ShippingTax);
    }

    [Fact]
    public async Task FlatRate_TaxShipping_AllTaxable_TaxesFullShippingAtStateRate()
    {
        var settings = """{"rates":[{"state":"CO","rate":0.029,"taxShipping":true}]}""";
        var result = await Flat(Cart(Item(price: 100m)), FlatCtx(settings, shipTo: Address("80202", "CO")), shipping: 10m);
        Assert.Equal(0.29m, result.ShippingTax);
        Assert.Equal(2.90m + 0.29m, result.TaxAmount);
        Assert.Equal(2.90m, result.LineTaxes!.Sum()); // line taxes are goods only
    }

    [Fact]
    public async Task FlatRate_TaxShipping_MixedCart_ProratesToTaxableShare()
    {
        // $60 taxable + $40 exempt → 60% of the $10 shipping is taxable → $6 × 10% = $0.60
        var settings = """{"rates":[{"state":"OR","rate":0.10,"taxShipping":true}]}""";
        var cart = Cart(Item(price: 60m), Item(price: 40m, taxExempt: true));
        var result = await Flat(cart, FlatCtx(settings, shipTo: Address()), shipping: 10m);
        Assert.Equal(0.60m, result.ShippingTax);
        Assert.Equal(6.00m + 0.60m, result.TaxAmount);
    }

    [Fact]
    public async Task FlatRate_TaxShipping_AllExempt_NoShippingTax()
    {
        var settings = """{"rates":[{"state":"OR","rate":0.10,"taxShipping":true}]}""";
        var result = await Flat(Cart(Item(price: 40m, taxExempt: true)), FlatCtx(settings, shipTo: Address()), shipping: 10m);
        Assert.Equal(0m, result.TaxAmount);
    }

    [Fact]
    public async Task FlatRate_UsesShipToStatesRate()
    {
        var result = await Flat(Cart(Item(price: 100m)), FlatCtx(CoUtRates, shipTo: Address("80202", "co")));
        Assert.Equal(2.90m, result.TaxAmount);
        Assert.Equal(0.029m, result.Rate);
        Assert.Equal(TaxCalculationStatus.Calculated, result.Status);
    }

    [Fact]
    public async Task FlatRate_UnlistedState_IsNotTaxed()
    {
        var result = await Flat(Cart(Item(price: 100m)), FlatCtx(CoUtRates, shipTo: Address("97470", "OR")));
        Assert.Equal(0m, result.TaxAmount);
        Assert.Equal(TaxCalculationStatus.Calculated, result.Status);
    }

    [Fact]
    public async Task FlatRate_FallsBackToBillingState()
    {
        var result = await Flat(Cart(Item(price: 100m)), FlatCtx(CoUtRates, billTo: Address("84037", "UT")));
        Assert.Equal(4.85m, result.TaxAmount);
    }

    [Fact]
    public async Task FlatRate_RatesButNoAddress_IsPendingAddress()
    {
        var result = await Flat(Cart(Item(price: 100m)), FlatCtx(CoUtRates));
        Assert.Equal(TaxCalculationStatus.PendingAddress, result.Status);
        Assert.Equal(0m, result.TaxAmount);
    }

    [Fact]
    public async Task FlatRate_NoRatesConfigured_IsZeroNotPending()
    {
        var result = await Flat(Cart(Item(price: 100m)), FlatCtx(null));
        Assert.Equal(0m, result.TaxAmount);
        Assert.Equal(TaxCalculationStatus.Calculated, result.Status);
    }

    [Fact]
    public async Task FlatRate_NoContext_UsesCartRate()
    {
        var result = await Flat(Cart(Item(price: 100m)) with { TaxRate = 0.05m }, null);
        Assert.Equal(5m, result.TaxAmount);
    }

    [Fact]
    public async Task FlatRate_LineTaxes_SumExactlyToTotal_ExemptLinesZero()
    {
        // 3 × 3.33 at 10% → total 1.00 (rounded once); naive per-line rounding would give 0.99.
        var cart = Cart(Item(price: 3.33m), Item(price: 3.33m), Item(price: 9m, taxExempt: true), Item(price: 3.33m));
        var result = await Flat(cart, FlatCtx("""{"rates":[{"state":"OR","rate":0.10}]}""", shipTo: Address()));

        Assert.Equal(1.00m, result.TaxAmount);
        Assert.Equal(result.TaxAmount, result.LineTaxes!.Sum());
        Assert.Equal(0m, result.LineTaxes![2]);
    }

    [Fact]
    public async Task Pricing_StoresProviderResult_PerItemShippingStatus()
    {
        var provider = new Mock<ITaxProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(TaxProviderKey.Avalara);
        TaxRequest? seen = null;
        provider.Setup(p => p.CalculateTaxAsync(It.IsAny<TaxRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TaxRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new TaxResult(0.08m, 4.50m, LineTaxes: [3m, 1m], ShippingTax: 0.50m));

        var pricing = new PricingService(new TaxProviderFactory([new FlatRateTaxProvider(), provider.Object]));
        var ctx = new TaxContext(Guid.NewGuid(), Guid.NewGuid(), TaxProviderKey.Avalara, null, Address(), null);

        var priced = await pricing.CalculateTotalsAsync(Cart(Item(price: 37.5m, shipping: 6.25m), Item(price: 12.5m)), ctx);

        Assert.Equal(6.25m, seen!.Shipping); // provider receives the computed shipping total
        Assert.Equal(TaxProviderKey.Avalara, priced.TaxProvider);
        Assert.Equal(4.50m, priced.SalesTax);
        Assert.Equal(0.50m, priced.ShippingTax);
        Assert.Equal([3m, 1m], priced.Items.Select(i => i.SalesTax));
        Assert.Equal(TaxCalculationStatus.Calculated, priced.TaxStatus);
        Assert.Equal(50m + 6.25m + 4.50m, priced.CartTotal);
    }

    [Fact]
    public async Task Pricing_ProviderError_ZeroTax_ClearsStaleItemTax_KeepsMessage()
    {
        var provider = new Mock<ITaxProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(TaxProviderKey.Avalara);
        provider.Setup(p => p.CalculateTaxAsync(It.IsAny<TaxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TaxResult.Zero(TaxCalculationStatus.Error, "Could not reach Avalara."));

        var pricing = new PricingService(new TaxProviderFactory([new FlatRateTaxProvider(), provider.Object]));
        var stale = Cart(Item(price: 10m) with { SalesTax = 0.80m });

        var priced = await pricing.CalculateTotalsAsync(stale,
            new TaxContext(Guid.NewGuid(), Guid.NewGuid(), TaxProviderKey.Avalara, null, null, null));

        Assert.Equal(0m, priced.SalesTax);
        Assert.Equal(0m, priced.Items[0].SalesTax);
        Assert.Equal(TaxCalculationStatus.Error, priced.TaxStatus);
        Assert.Equal("Could not reach Avalara.", priced.TaxMessage);
    }
}
