using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Credentials;

/// <summary>
/// Redis-backed caching decorator over the real (Key Vault) IPortalCredentialStore — portal
/// credentials have no tenant scoping, so the cache key is just the key name. See
/// CredentialCacheSupport for the shared caching mechanics and CachedTenantCredentialStore for
/// the tenant-scoped equivalent.
/// </summary>
internal class CachedPortalCredentialStore(
    [FromKeyedServices("keyvault")] IPortalCredentialStore inner,
    IConnectionMultiplexer redis)
    : IPortalCredentialStore
{
    private static string Key(string keyName) => $"cred:portal:{keyName}";

    public async Task<string?> GetAsync(string keyName, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = Key(keyName);

        var (found, cachedValue) = await CredentialCacheSupport.TryGetAsync(db, key);
        if (found) return cachedValue;

        var value = await inner.GetAsync(keyName, ct);
        await CredentialCacheSupport.StoreAsync(db, key, value);
        return value;
    }

    public async Task SetAsync(string keyName, string value, CancellationToken ct = default)
    {
        await inner.SetAsync(keyName, value, ct);
        await CredentialCacheSupport.EvictAsync(redis.GetDatabase(), Key(keyName));
    }

    public async Task DeleteAsync(string keyName, CancellationToken ct = default)
    {
        await inner.DeleteAsync(keyName, ct);
        await CredentialCacheSupport.EvictAsync(redis.GetDatabase(), Key(keyName));
    }

    // Admin UI listing — low frequency, always wants a fresh read. Not cached.
    public Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken ct = default) =>
        inner.ListAsync(ct);
}
