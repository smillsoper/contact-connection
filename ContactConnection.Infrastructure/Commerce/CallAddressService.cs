using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Infrastructure.Commerce;

/// <summary>See ICallAddressService.</summary>
public class CallAddressService(
    ICallRecordRepository callRecords, ICartService carts, IMediaReattributionService? media = null) : ICallAddressService
{
    public async Task<CartDocument?> SetAsync(
        Guid callRecordId, string role, AddressData address, CancellationToken ct = default)
    {
        if (role == CallAddressRole.None) return null;
        if (!CallAddressRole.IsValid(role))
            throw new ArgumentException($"Unknown address role '{role}'.", nameof(role));

        var record = await callRecords.GetByIdWithInteractionsAsync(callRecordId, ct)
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

        // Media (S171, Phase B): a captured zip places a Local media call at the station nearest the
        // caller — at arrival only the phone's area code was known. Never blocks the address save.
        if (media is not null && (updated.Billing ?? updated.Shipping)?.Zip is { } zip)
        {
            try
            {
                if (await media.ForZipAsync(record, zip, ct) is { } attribution) record.SetMediaAttribution(attribution);
            }
            catch (Exception) { /* keep the existing attribution */ }
        }
        await callRecords.SaveChangesAsync(ct);

        // Tax depends on the ship-to address — re-price. A billing-only change can matter too when
        // there's no shipping address (providers fall back to billing), so re-price in that case.
        var affectsTax = setsShipping || current?.Shipping is null;
        if (!affectsTax || !record.Interactions.Any(i => i.Cart is { Items.Count: > 0 })) return null;

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

    public async Task<string?> SetPracticeDnisAsync(Guid callRecordId, string dnis, CancellationToken ct = default)
    {
        var record = await callRecords.GetByIdAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found.");
        if (!record.TrySetPracticeDnis(dnis)) return null;
        await callRecords.SaveChangesAsync(ct);
        return record.Dnis;
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
