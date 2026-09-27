using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Infrastructure.Tests.Commerce;

internal static class TaxTestData
{
    public static CartItem Item(string sku = "SKU001", decimal price = 10m, int qty = 1,
        bool taxExempt = false, decimal shipping = 0m, string description = "Test Product", string? taxCode = null) => new(
        OfferId: Guid.NewGuid(), ProductId: Guid.NewGuid(), Sku: sku, Description: description,
        Quantity: qty, FullPrice: price, ExtendedPrice: price * qty, Shipping: shipping, Weight: 0m,
        SalesTax: 0m, ShippingExempt: false, TaxExempt: taxExempt, OnBackOrder: false,
        AutoShip: false, AutoShipIntervalDays: 0, IsUpsell: false, UpsellQty: 0, MixMatchCode: null,
        ShipMethod: null, DeliveryMessage: null, ShipToJson: null, Payments: [],
        PersonalizationAnswers: [], KitSelections: [], CanadaSurcharge: 0m, AKHISurcharge: 0m,
        OutlyingUSSurcharge: 0m, ForeignSurcharge: 0m, TaxCode: taxCode);

    public static CartDocument Cart(params CartItem[] items)
        => CartDocument.Empty() with { Items = [.. items] };

    public static AddressData Address(string zip = "97470", string state = "OR") => new()
    {
        FirstName = "Test", LastName = "Order", Street = "435 NE Casper St",
        City = "Roseburg", State = state, Zip = zip, Country = "US",
    };
}
