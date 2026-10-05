using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Credentials;

/// <summary>
/// Campaign -> client -> tenant credential cascade for vendor integrations (payment gateways, tax
/// providers) — the same precedence CustomFieldDefinition uses for scope resolution. Key naming
/// convention in the tenant credential store:
///   {vendor}:{campaignId}:{field}   campaign-specific (e.g. Life Seasons runs a separate
///                                   Authorize.Net merchant account per campaign)
///   {vendor}:{clientId}:{field}     client-wide
///   {vendor}:{field}                tenant-wide default
/// </summary>
public static class ScopedCredentials
{
    public static async Task<string?> ResolveAsync(
        ITenantCredentialStore credentials, string vendor, string field,
        Guid campaignId, Guid clientId, CancellationToken ct = default)
    {
        // A sandbox run (CredentialSetScope) reads the campaign's sandbox set and never falls back to production keys.
        vendor = CredentialSetScope.VendorKey(vendor);
        if (campaignId != Guid.Empty)
        {
            var campaignValue = await credentials.GetAsync($"{vendor}:{campaignId}:{field}", ct);
            if (campaignValue is not null) return campaignValue;
        }

        if (clientId != Guid.Empty)
        {
            var clientValue = await credentials.GetAsync($"{vendor}:{clientId}:{field}", ct);
            if (clientValue is not null) return clientValue;
        }

        return await credentials.GetAsync($"{vendor}:{field}", ct);
    }
}
