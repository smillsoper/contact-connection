using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>S169: offer SKU override (fulfillment variants) and client/campaign scope on products.</summary>
public class CatalogScopeAndSkuTests
{
    private static Offer OfferOn(Product product)
    {
        var offer = Offer.Create(product.TenantId, product.Id, "Buy 2 Get 1 Free", 139.90m);
        typeof(Offer).GetProperty(nameof(Offer.Product))!.SetValue(offer, product);
        return offer;
    }

    [Fact]
    public void EffectiveSku_IsTheProductSku_WhenNoOverride()
    {
        var offer = OfferOn(Product.Create(Guid.NewGuid(), "283", "NeuroQ Memory & Focus"));
        Assert.Null(offer.Sku);
        Assert.Equal("283", offer.EffectiveSku);
    }

    [Fact]
    public void EffectiveSku_IsTheOverride_WhenSet()
    {
        var offer = OfferOn(Product.Create(Guid.NewGuid(), "283", "NeuroQ Memory & Focus"));
        offer.SetSku("  283-3-CTY-90 ");
        Assert.Equal("283-3-CTY-90", offer.EffectiveSku);
    }

    [Fact]
    public void BlankSku_ClearsTheOverride()
    {
        var offer = OfferOn(Product.Create(Guid.NewGuid(), "283", "NeuroQ Memory & Focus"));
        offer.SetSku("283-3-CTY-90");
        offer.SetSku("  ");
        Assert.Null(offer.Sku);
        Assert.Equal("283", offer.EffectiveSku);
    }

    [Fact]
    public void Product_CampaignScope_RequiresItsClient()
    {
        var product = Product.Create(Guid.NewGuid(), "283", "NeuroQ Memory & Focus");
        Assert.Throws<ArgumentException>(() => product.SetScope(null, [Guid.NewGuid()]));

        var client = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        product.SetScope(client, [c1, c2, c1]);
        Assert.Equal(client, product.ClientId);
        Assert.Equal([c1, c2], product.CampaignIds);

        product.SetScope(null);
        Assert.Null(product.ClientId);
        Assert.Empty(product.CampaignIds);
    }
}
