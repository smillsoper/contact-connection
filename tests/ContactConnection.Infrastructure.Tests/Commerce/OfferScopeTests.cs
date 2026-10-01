using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Commerce;

/// <summary>Which offers an agent may add to a call's cart (S169): tenant-wide offers fit any call;
/// client- or campaign-scoped offers only fit calls on that client / campaign.</summary>
public class OfferScopeTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), ClientA = Guid.NewGuid(), ClientB = Guid.NewGuid(),
        CampaignA1 = Guid.NewGuid(), CampaignA2 = Guid.NewGuid();

    private static Offer OfferScoped(Guid? client, params Guid[] campaigns)
    {
        var o = Offer.Create(Tenant, Guid.NewGuid(), "Offer", 10m);
        if (client is not null) o.SetScope(client, campaigns);
        return o;
    }

    private static CallRecord Call(Guid client, Guid campaign)
        => CallRecord.Create(Tenant, client, campaign, CallSource.Inbound, "+15551234567");

    [Fact]
    public void TenantWideOffer_FitsAnyCall()
    {
        Assert.True(CartService.OfferFitsCall(OfferScoped(null), Call(ClientA, CampaignA1)));
        Assert.True(CartService.OfferFitsCall(OfferScoped(null), Call(Guid.Empty, Guid.Empty)));
    }

    [Fact]
    public void ClientScopedOffer_FitsOnlyThatClientsCalls()
    {
        var offer = OfferScoped(ClientA);
        Assert.True(CartService.OfferFitsCall(offer, Call(ClientA, CampaignA2)));
        Assert.False(CartService.OfferFitsCall(offer, Call(ClientB, Guid.NewGuid())));
        Assert.False(CartService.OfferFitsCall(offer, Call(Guid.Empty, Guid.Empty)));
    }

    [Fact]
    public void CampaignScopedOffer_FitsOnlyThatCampaignsCalls()
    {
        var offer = OfferScoped(ClientA, CampaignA1);
        Assert.True(CartService.OfferFitsCall(offer, Call(ClientA, CampaignA1)));
        Assert.False(CartService.OfferFitsCall(offer, Call(ClientA, CampaignA2)));
    }

    [Fact]
    public void MultiCampaignOffer_FitsEachListedCampaign_AndNoOther()
    {
        // e.g. a NeuroQ offer on NeuroQ V1 and NeuroQ Customer Service, not on My Best Heart (same client)
        var myBestHeart = Guid.NewGuid();
        var offer = OfferScoped(ClientA, CampaignA1, CampaignA2);
        Assert.True(CartService.OfferFitsCall(offer, Call(ClientA, CampaignA1)));
        Assert.True(CartService.OfferFitsCall(offer, Call(ClientA, CampaignA2)));
        Assert.False(CartService.OfferFitsCall(offer, Call(ClientA, myBestHeart)));
        Assert.False(CartService.OfferFitsCall(offer, Call(ClientA, Guid.Empty)));
    }
}
