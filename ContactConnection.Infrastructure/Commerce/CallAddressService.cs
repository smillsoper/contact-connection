using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Infrastructure.Commerce;

/// <summary>See ICallAddressService.</summary>
public class CallAddressService(ICallRecordRepository callRecords, ICartService carts) : ICallAddressService
{
    public async Task<CartDocument?> SetAsync(
        Guid callRecordId, string role, AddressData address, CancellationToken ct = default)
    {
        if (role == CallAddressRole.None) return null;
        if (!CallAddressRole.IsValid(role))
            throw new ArgumentException($"Unknown address role '{role}'.", nameof(role));

        var record = await callRecords.GetByIdAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found.");

        // Always assign a NEW CallAddresses — the JSONB column has no value comparer, so EF only
        // notices a changed reference, never an in-place mutation.
        var current = record.Addresses;
        var setsShipping = role is CallAddressRole.Shipping or CallAddressRole.BillingAndShipping;
        var updated = new CallAddresses
        {
            Billing  = role is CallAddressRole.Billing or CallAddressRole.BillingAndShipping ? address : current?.Billing,
            Shipping = setsShipping ? address : current?.Shipping,
        };
        record.SetAddresses(updated);
        await callRecords.SaveChangesAsync(ct);

        // Tax depends on the ship-to address — re-price. A billing-only change can matter too when
        // there's no shipping address (providers fall back to billing), so re-price in that case.
        var affectsTax = setsShipping || current?.Shipping is null;
        if (!affectsTax || record.Cart is null || record.Cart.Items.Count == 0) return null;

        var result = await carts.RecalculateAsync(callRecordId, ct);
        return result.Cart;
    }
}
