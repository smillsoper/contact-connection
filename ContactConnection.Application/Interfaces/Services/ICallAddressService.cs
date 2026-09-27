using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>Which of the call record's addresses an address node's result is saved as.</summary>
public static class CallAddressRole
{
    public const string None               = "none";
    public const string Billing            = "billing";
    public const string Shipping           = "shipping";
    public const string BillingAndShipping = "billing_and_shipping";

    public static bool IsValid(string role) =>
        role is None or Billing or Shipping or BillingAndShipping;
}

/// <summary>
/// Writes captured addresses onto CallRecord.Addresses — the call's single source of truth for
/// billing/shipping (order APIs, tax, fulfillment read from there, not from flow variables). When
/// the shipping address changes it re-prices the cart, since tax depends on the ship-to address.
/// </summary>
public interface ICallAddressService
{
    /// <summary>Saves <paramref name="address"/> under <paramref name="role"/> (a CallAddressRole
    /// value; "none" is a no-op). Returns the re-priced cart when the shipping address changed and
    /// the call has a cart, otherwise null.</summary>
    Task<Domain.ValueObjects.Commerce.CartDocument?> SetAsync(
        Guid callRecordId, string role, AddressData address, CancellationToken ct = default);
}
