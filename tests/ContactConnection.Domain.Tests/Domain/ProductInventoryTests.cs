using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

public class ProductInventoryTests
{
    private static Product MakeProduct(
        ProductInventoryStatus status = ProductInventoryStatus.Available,
        int qtyAvailable = 10,
        int qtyReserved = 0,
        bool decrementOnOrder = true,
        int minimumQty = 0)
    {
        var p = Product.Create(Guid.NewGuid(), "SKU001", "Test Product");
        p.SetInventory(status, qtyAvailable, decrementOnOrder, minimumQty);
        // Simulate pre-existing reservations via Reserve calls
        for (var i = 0; i < qtyReserved; i++)
            p.Reserve(1);
        return p;
    }

    // ── CanAddToCart ──────────────────────────────────────────────────────────

    [Fact]
    public void CanAddToCart_Available_AlwaysTrue()
    {
        var p = MakeProduct(ProductInventoryStatus.Available, qtyAvailable: 0);
        Assert.True(p.CanAddToCart(100));
    }

    [Fact]
    public void CanAddToCart_CanBackorder_AlwaysTrue()
    {
        var p = MakeProduct(ProductInventoryStatus.CanBackorder, qtyAvailable: 0);
        Assert.True(p.CanAddToCart(100));
    }

    [Fact]
    public void CanAddToCart_Discontinued_AlwaysFalse()
    {
        var p = MakeProduct(ProductInventoryStatus.Discontinued, qtyAvailable: 999);
        Assert.False(p.CanAddToCart(1));
    }

    [Fact]
    public void CanAddToCart_OutOfStock_AlwaysFalse()
    {
        var p = MakeProduct(ProductInventoryStatus.OutOfStock, qtyAvailable: 999);
        Assert.False(p.CanAddToCart(1));
    }

    [Fact]
    public void CanAddToCart_NoBackorder_SufficientNetStock_ReturnsTrue()
    {
        // QtyAvailable=10, QtyReserved=3 → net=7; requesting 5 → 7-5=2 >= minimumQty(0) → true
        var p = MakeProduct(ProductInventoryStatus.NoBackorder, qtyAvailable: 10, qtyReserved: 3);
        Assert.True(p.CanAddToCart(5));
    }

    [Fact]
    public void CanAddToCart_NoBackorder_InsufficientNetStock_ReturnsFalse()
    {
        // QtyAvailable=5, QtyReserved=3 → net=2; requesting 3 → 2-3=-1 < 0 → false
        var p = MakeProduct(ProductInventoryStatus.NoBackorder, qtyAvailable: 5, qtyReserved: 3);
        Assert.False(p.CanAddToCart(3));
    }

    [Fact]
    public void CanAddToCart_NoBackorder_MinimumQtyEnforced()
    {
        // QtyAvailable=10, QtyReserved=0, minimumQty=2; requesting 9 → net 10-9=1 < 2 → false
        var p = MakeProduct(ProductInventoryStatus.NoBackorder, qtyAvailable: 10, minimumQty: 2);
        Assert.False(p.CanAddToCart(9));
    }

    // ── Reserve ───────────────────────────────────────────────────────────────

    [Fact]
    public void Reserve_Available_IncrementsQtyReserved()
    {
        var p = MakeProduct(ProductInventoryStatus.Available, qtyAvailable: 10);
        var result = p.Reserve(3);
        Assert.True(result);
        Assert.Equal(3, p.QtyReserved);
        Assert.Equal(10, p.QtyAvailable); // not decremented yet
    }

    [Fact]
    public void Reserve_Discontinued_ReturnsFalseNoChange()
    {
        var p = MakeProduct(ProductInventoryStatus.Discontinued, qtyAvailable: 100);
        var result = p.Reserve(1);
        Assert.False(result);
        Assert.Equal(0, p.QtyReserved);
    }

    [Fact]
    public void Reserve_OutOfStock_ReturnsFalseNoChange()
    {
        var p = MakeProduct(ProductInventoryStatus.OutOfStock, qtyAvailable: 100);
        var result = p.Reserve(1);
        Assert.False(result);
        Assert.Equal(0, p.QtyReserved);
    }

    [Fact]
    public void Reserve_NoBackorder_InsufficientStock_ReturnsFalseNoChange()
    {
        var p = MakeProduct(ProductInventoryStatus.NoBackorder, qtyAvailable: 2);
        var result = p.Reserve(5);
        Assert.False(result);
        Assert.Equal(0, p.QtyReserved);
    }

    [Fact]
    public void Reserve_WhenDecrementOnOrder_False_DoesNotTrackReservation()
    {
        var p = MakeProduct(ProductInventoryStatus.Available, qtyAvailable: 10, decrementOnOrder: false);
        var result = p.Reserve(5);
        Assert.True(result);
        Assert.Equal(0, p.QtyReserved); // not tracked when DecrementOnOrder is false
    }

    // ── Release ───────────────────────────────────────────────────────────────

    [Fact]
    public void Release_DecrementsQtyReserved()
    {
        var p = MakeProduct(ProductInventoryStatus.Available, qtyAvailable: 10);
        p.Reserve(5);
        p.Release(3);
        Assert.Equal(2, p.QtyReserved);
    }

    [Fact]
    public void Release_DoesNotGoBelowZero()
    {
        var p = MakeProduct(ProductInventoryStatus.Available, qtyAvailable: 10);
        p.Reserve(2);
        p.Release(10); // releasing more than reserved
        Assert.Equal(0, p.QtyReserved);
    }

    [Fact]
    public void Release_WhenDecrementOnOrder_False_NoChange()
    {
        var p = MakeProduct(ProductInventoryStatus.Available, qtyAvailable: 10, decrementOnOrder: false);
        p.Release(5);
        Assert.Equal(0, p.QtyReserved);
        Assert.Equal(10, p.QtyAvailable);
    }

    // ── Confirm ───────────────────────────────────────────────────────────────

    [Fact]
    public void Confirm_DecrementsAvailableAndReserved()
    {
        var p = MakeProduct(ProductInventoryStatus.Available, qtyAvailable: 10);
        p.Reserve(3);
        p.Confirm(3);
        Assert.Equal(7, p.QtyAvailable);
        Assert.Equal(0, p.QtyReserved);
    }

    [Fact]
    public void Confirm_FloorsAvailableAndReservedAtZero()
    {
        var p = MakeProduct(ProductInventoryStatus.Available, qtyAvailable: 2);
        p.Reserve(2);
        p.Confirm(10); // confirm more than available
        Assert.Equal(0, p.QtyAvailable);
        Assert.Equal(0, p.QtyReserved);
    }

    [Fact]
    public void Confirm_WhenDecrementOnOrder_False_NoChange()
    {
        var p = MakeProduct(ProductInventoryStatus.Available, qtyAvailable: 10, decrementOnOrder: false);
        p.Confirm(5);
        Assert.Equal(10, p.QtyAvailable);
        Assert.Equal(0, p.QtyReserved);
    }
}
