using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;
using ContactConnection.Infrastructure.Commerce;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Commerce;

/// <summary>
/// Interaction-scoped commerce, phase 3 (S178): the cart belongs to an interaction. A sales agent's cart and a CS agent's
/// cart on the same transferred call stay separate; the first interaction's cart is mirrored onto the record's legacy
/// column until the readers move (phase 4).
/// </summary>
public class CartServiceInteractionTests
{
    private readonly Mock<ICallRecordRepository> _records = new();
    private readonly Mock<IPricingService> _pricing = new();
    private readonly CallRecord _record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private readonly CallInteraction _sales;
    private readonly CallInteraction _cs;

    public CartServiceInteractionTests()
    {
        _sales = _record.AddInteraction(InteractionType.CustomerService);
        _sales.AssignTo(Guid.NewGuid(), _record.CampaignId);
        _cs = _record.AddInteraction(InteractionType.CustomerService);
        _cs.AssignTo(Guid.NewGuid(), Guid.NewGuid());
        _records.Setup(r => r.GetByIdWithInteractionsAsync(_record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_record);
        // Pricing returns the cart as given, so the test can tell carts apart by TaxRate.
        _pricing.Setup(p => p.CalculateTotalsAsync(It.IsAny<CartDocument>(), It.IsAny<TaxContext?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CartDocument c, TaxContext? _, CancellationToken _) => c);
    }

    private CartService Service()
    {
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(i => i.ReserveCartAsync(It.IsAny<CartDocument>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return new CartService(_records.Object, new Mock<IOfferRepository>().Object, inventory.Object, _pricing.Object,
            new Mock<ICampaignRepository>().Object);
    }

    private static CartDocument Cart(decimal marker) => CartDocument.Empty() with { TaxRate = marker };

    [Fact]
    public async Task CsInteraction_GetsItsOwnCart_SalesCartAndRecordUntouched()
    {
        await Service().ReplaceCartAsync(_record.Id, Cart(0.01m), interactionId: _sales.Id);
        await Service().ReplaceCartAsync(_record.Id, Cart(0.02m), interactionId: _cs.Id);

        Assert.Equal(0.01m, _sales.Cart!.TaxRate);
        Assert.Equal(0.02m, _cs.Cart!.TaxRate);
        Assert.Equal(0.01m, _record.Cart!.TaxRate);     // mirrors the first interaction only
    }

    [Fact]
    public async Task NoInteractionGiven_UsesTheCallsCurrentActiveInteraction()
    {
        _sales.Complete("Transferred to Customer Service");   // sales finished; CS still active

        await Service().ReplaceCartAsync(_record.Id, Cart(0.03m));

        Assert.Equal(0.03m, _cs.Cart!.TaxRate);
        Assert.Null(_sales.Cart);
    }

    [Fact]
    public async Task Recalculate_WithoutAnInteraction_RepricesEveryInteractionsCart()
    {
        _sales.SetCart(Cart(0.01m));
        _cs.SetCart(Cart(0.02m));

        await Service().RecalculateAsync(_record.Id);

        _pricing.Verify(p => p.CalculateTotalsAsync(It.Is<CartDocument>(c => c.TaxRate == 0.01m), It.IsAny<TaxContext?>(), It.IsAny<CancellationToken>()), Times.Once);
        _pricing.Verify(p => p.CalculateTotalsAsync(It.Is<CartDocument>(c => c.TaxRate == 0.02m), It.IsAny<TaxContext?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
