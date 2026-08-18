using ContactConnection.Infrastructure.Credentials;
using StackExchange.Redis;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Credentials;

/// <summary>Covers the shared Redis caching mechanics (TryGetAsync/StoreAsync/EvictAsync) that
/// both CachedTenantCredentialStore and CachedPortalCredentialStore build on — the miss/hit/
/// absent-sentinel/eviction/TTL contract. See RedisFixture for why this hits a real Redis rather
/// than a mock.</summary>
[Collection("Redis")]
public class CredentialCacheSupportTests(RedisFixture fixture)
{
    private IDatabase Db => fixture.Connection.GetDatabase();

    private static string NewKey() => $"cc-test:{Guid.NewGuid()}";

    [Fact]
    public async Task TryGetAsync_KeyNeverSet_IsAMiss()
    {
        var found = await CredentialCacheSupport.TryGetAsync(Db, NewKey());
        Assert.False(found.Found);
        Assert.Null(found.Value);
    }

    [Fact]
    public async Task StoreThenTryGet_RealValue_RoundTrips()
    {
        var key = NewKey();
        try
        {
            await CredentialCacheSupport.StoreAsync(Db, key, "the-secret-value");

            var result = await CredentialCacheSupport.TryGetAsync(Db, key);
            Assert.True(result.Found);
            Assert.Equal("the-secret-value", result.Value);
        }
        finally { await Db.KeyDeleteAsync(key); }
    }

    [Fact]
    public async Task StoreThenTryGet_NullValue_CachedAsFoundButNull()
    {
        // "Not found" is itself cached (via a sentinel) so a misconfigured credential doesn't hit
        // Key Vault on every single call — TryGet must distinguish this from an actual miss.
        var key = NewKey();
        try
        {
            await CredentialCacheSupport.StoreAsync(Db, key, null);

            var result = await CredentialCacheSupport.TryGetAsync(Db, key);
            Assert.True(result.Found);
            Assert.Null(result.Value);
        }
        finally { await Db.KeyDeleteAsync(key); }
    }

    [Fact]
    public async Task EvictAsync_RemovesCachedValue_SubsequentTryGetIsAMissAgain()
    {
        var key = NewKey();
        await CredentialCacheSupport.StoreAsync(Db, key, "will-be-evicted");

        await CredentialCacheSupport.EvictAsync(Db, key);

        var result = await CredentialCacheSupport.TryGetAsync(Db, key);
        Assert.False(result.Found);
    }

    [Fact]
    public async Task StoreAsync_SetsATtl_NotPersistedForever()
    {
        var key = NewKey();
        try
        {
            await CredentialCacheSupport.StoreAsync(Db, key, "expires-eventually");

            var ttl = await Db.KeyTimeToLiveAsync(key);
            Assert.NotNull(ttl);
            Assert.True(ttl.Value > TimeSpan.Zero);
            Assert.True(ttl.Value <= CredentialCacheSupport.Ttl);
        }
        finally { await Db.KeyDeleteAsync(key); }
    }
}
