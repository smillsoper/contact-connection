using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;
using ContactConnection.Infrastructure.Commerce;
using Moq;
using Xunit;
using static ContactConnection.Infrastructure.Tests.Commerce.TaxTestData;

namespace ContactConnection.Infrastructure.Tests.Commerce;

public class CallAddressServiceTests
{
    private static (CallAddressService Service, Mock<ICartService> Carts, CallRecord Record) Setup(bool withCart)
    {
        var record = CallRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        if (withCart) record.SetCart(Cart(Item()));
        var repo = new Mock<ICallRecordRepository>();
        repo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var carts = new Mock<ICartService>();
        carts.Setup(c => c.RecalculateAsync(record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CartOperationResult.Success(CartDocument.Empty()));
        return (new CallAddressService(repo.Object, carts.Object), carts, record);
    }

    [Fact]
    public async Task Shipping_SetsShippingOnly_KeepsExistingBilling_AndRecalculates()
    {
        var (service, carts, record) = Setup(withCart: true);
        var billing = Address("10001", "NY");
        record.SetAddresses(new CallAddresses { Billing = billing });

        await service.SetAsync(record.Id, CallAddressRole.Shipping, Address("80202", "CO"));

        Assert.Same(billing, record.Addresses!.Billing);
        Assert.Equal("80202", record.Addresses.Shipping!.Zip);
        carts.Verify(c => c.RecalculateAsync(record.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BillingAndShipping_SetsBoth()
    {
        var (service, _, record) = Setup(withCart: false);
        var address = Address();

        await service.SetAsync(record.Id, CallAddressRole.BillingAndShipping, address);

        Assert.Same(address, record.Addresses!.Billing);
        Assert.Same(address, record.Addresses.Shipping);
    }

    [Fact]
    public async Task Billing_WhenShippingAlreadySet_DoesNotRecalculate()
    {
        var (service, carts, record) = Setup(withCart: true);
        record.SetAddresses(new CallAddresses { Shipping = Address("80202", "CO") });

        await service.SetAsync(record.Id, CallAddressRole.Billing, Address());

        carts.Verify(c => c.RecalculateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Billing_WithNoShipping_Recalculates_SinceTaxFallsBackToBilling()
    {
        var (service, carts, record) = Setup(withCart: true);

        await service.SetAsync(record.Id, CallAddressRole.Billing, Address());

        carts.Verify(c => c.RecalculateAsync(record.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyCart_NeverRecalculates()
    {
        var (service, carts, record) = Setup(withCart: false);
        await service.SetAsync(record.Id, CallAddressRole.Shipping, Address());
        carts.Verify(c => c.RecalculateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task None_IsNoOp_AndUnknownRoleThrows()
    {
        var (service, _, record) = Setup(withCart: true);
        Assert.Null(await service.SetAsync(record.Id, CallAddressRole.None, Address()));
        Assert.Null(record.Addresses);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(record.Id, "home", Address()));
    }
}
