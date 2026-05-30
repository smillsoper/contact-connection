using Azure.Security.KeyVault.Secrets;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;

namespace ContactConnection.Infrastructure.Credentials;

internal class KeyVaultTenantCredentialStore : KeyVaultCredentialStoreBase, ITenantCredentialStore
{
    private readonly TenantContext _tenantContext;

    // Prefix is dynamic — computed from the resolved tenant subdomain.
    protected override string Prefix =>
        $"tenant--{Sanitize(_tenantContext.Current?.Subdomain ?? "unknown")}--";

    public KeyVaultTenantCredentialStore(SecretClient client, TenantContext tenantContext)
        : base(client)
    {
        _tenantContext = tenantContext;
    }

    Task<string?> ITenantCredentialStore.GetAsync(string keyName, CancellationToken ct) => GetAsync(keyName, ct);
    Task ITenantCredentialStore.SetAsync(string keyName, string value, CancellationToken ct) => SetAsync(keyName, value, ct);
    Task ITenantCredentialStore.DeleteAsync(string keyName, CancellationToken ct) => DeleteAsync(keyName, ct);
    Task<IReadOnlyList<CredentialSummary>> ITenantCredentialStore.ListAsync(CancellationToken ct) => ListAsync(ct);
}
