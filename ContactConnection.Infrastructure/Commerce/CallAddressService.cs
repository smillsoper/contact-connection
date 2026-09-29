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

    public async Task SetEmailAsync(Guid callRecordId, string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        var record = await callRecords.GetByIdAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found.");
        record.SetEmail(email.Trim());
        await callRecords.SaveChangesAsync(ct);
    }

    public async Task SetNameAsync(Guid callRecordId, string? firstName, string? lastName, CancellationToken ct = default)
    {
        firstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim();
        lastName  = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();
        if (firstName is null && lastName is null) return;

        var record = await callRecords.GetByIdAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found.");
        record.SetCallerIdentity(firstName ?? record.FirstName, lastName ?? record.LastName,
            record.Email, record.Phone, record.AccountNumber);
        await callRecords.SaveChangesAsync(ct);
    }

    public async Task SetPhoneAsync(Guid callRecordId, string role, string phone, CancellationToken ct = default)
    {
        if (role == CallAddressRole.None || string.IsNullOrWhiteSpace(phone)) return;
        if (!CallAddressRole.IsValid(role))
            throw new ArgumentException($"Unknown phone role '{role}'.", nameof(role));

        var record = await callRecords.GetByIdAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found.");

        var billing  = role is CallAddressRole.Billing or CallAddressRole.BillingAndShipping ? phone : record.BillingPhone;
        var shipping = role is CallAddressRole.Shipping or CallAddressRole.BillingAndShipping ? phone : record.ShippingPhone;
        record.SetContactPhones(billing, shipping);
        await callRecords.SaveChangesAsync(ct);
    }
}
