using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Credentials;

/// <summary>
/// Redis-backed caching decorator over the real (Key Vault) ITenantCredentialStore — see
/// CredentialCacheSupport for the caching mechanics. Registered in front of the keyed
/// "keyvault"-tagged KeyVaultTenantCredentialStore (see ServiceCollectionExtensions); only
/// wired up when Key Vault is actually configured — nothing worth caching in front of
/// NullCredentialStore, which already never hits the network.
///
/// GetAsync scopes its cache key off the ambient TenantContext, same as
/// KeyVaultTenantCredentialStore itself does for its Key Vault secret name prefix.
/// GetForTenantAsync is scoped off its explicit tenantSubdomain parameter instead — it exists
/// specifically for callers with no ambient TenantContext (background services).
/// </summary>
internal class CachedTenantCredentialStore(
    [FromKeyedServices("keyvault")] ITenantCredentialStore inner,
    IConnectionMultiplexer redis,
    TenantContext tenantContext)
    : ITenantCredentialStore
{
    private static string Key(string subdomain, string keyName) => $"cred:tenant:{subdomain}:{keyName}";

    public Task<string?> GetAsync(string keyName, CancellationToken ct = default) =>
        GetCachedAsync(tenantContext.Current?.Subdomain ?? "unknown", keyName, ct,
            (k, c) => inner.GetAsync(k, c));

    public Task<string?> GetForTenantAsync(string tenantSubdomain, string keyName, CancellationToken ct = default) =>
        GetCachedAsync(tenantSubdomain, keyName, ct,
            (k, c) => inner.GetForTenantAsync(tenantSubdomain, k, c));

    private async Task<string?> GetCachedAsync(
        string subdomain, string keyName, CancellationToken ct,
        Func<string, CancellationToken, Task<string?>> loadAsync)
    {
        var db = redis.GetDatabase();
        var key = Key(subdomain, keyName);

        var (found, cachedValue) = await CredentialCacheSupport.TryGetAsync(db, key);
        if (found) return cachedValue;

        var value = await loadAsync(keyName, ct);
        await CredentialCacheSupport.StoreAsync(db, key, value);
        return value;
    }

    public async Task SetAsync(string keyName, string value, CancellationToken ct = default)
    {
        await inner.SetAsync(keyName, value, ct);
        await CredentialCacheSupport.EvictAsync(
            redis.GetDatabase(), Key(tenantContext.Current?.Subdomain ?? "unknown", keyName));
    }

    public async Task DeleteAsync(string keyName, CancellationToken ct = default)
    {
        await inner.DeleteAsync(keyName, ct);
        await CredentialCacheSupport.EvictAsync(
            redis.GetDatabase(), Key(tenantContext.Current?.Subdomain ?? "unknown", keyName));
    }

    // Admin UI listing — low frequency, always wants a fresh read. Not cached.
    public Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken ct = default) =>
        inner.ListAsync(ct);
}
