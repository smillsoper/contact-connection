using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace ContactConnection.Infrastructure.Extensions;

/// <summary>
/// Resolves the credential used to authenticate to Azure Key Vault — shared by the
/// credential-store registration (ServiceCollectionExtensions) and the Key Vault
/// configuration provider (Api/Worker Program.cs) so both use identical logic.
///
/// Prefers an explicit EntraId:ClientSecret when one is configured: this is how local
/// dev authenticates (the app registration's client secret, kept in User Secrets, never
/// committed) — DefaultAzureCredential has no valid fallback on a bare dev machine with
/// no `az login` session and no Managed Identity endpoint. Production should NOT configure
/// EntraId:ClientSecret, so it falls through to DefaultAzureCredential there — Managed
/// Identity, zero secrets required.
/// </summary>
public static class AzureCredentialFactory
{
    public static TokenCredential Resolve(IConfiguration configuration)
    {
        var clientSecret = configuration["EntraId:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
            return new DefaultAzureCredential();

        var tenantId = configuration["EntraId:TenantId"]
            ?? throw new InvalidOperationException("EntraId:TenantId is not configured.");
        var clientId = configuration["EntraId:ClientId"]
            ?? throw new InvalidOperationException("EntraId:ClientId is not configured.");

        return new ClientSecretCredential(tenantId, clientId, clientSecret);
    }
}
