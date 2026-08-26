using ContactConnection.Infrastructure.Credentials;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Credentials;

/// <summary>Covers RedisOAuth2TokenCache — a plain TTL-bounded token cache (no absent-sentinel
/// semantics like CredentialCacheSupport, since a missing oauth2 token just means "go exchange
/// for a new one", not "a misconfigured credential").</summary>
[Collection("Redis")]
public class RedisOAuth2TokenCacheTests(RedisFixture fixture)
{
    private RedisOAuth2TokenCache CreateCache() => new(fixture.Connection);

    private static string NewKey() => $"oauth2-test:{Guid.NewGuid()}";

    [Fact]
    public async Task GetAsync_NeverSet_ReturnsNull()
    {
        var cache = CreateCache();
        Assert.Null(await cache.GetAsync(NewKey()));
    }

    [Fact]
    public async Task SetThenGet_RoundTripsTheToken()
    {
        var cache = CreateCache();
        var key = NewKey();

        await cache.SetAsync(key, "access-token-abc123", TimeSpan.FromMinutes(5));

        Assert.Equal("access-token-abc123", await cache.GetAsync(key));
    }

    [Fact]
    public async Task SetAsync_ZeroOrNegativeTtl_StoresNothing()
    {
        // A vendor that returns expires_in <= 0 (or an already-expired token) must never be
        // cached — the next call should always re-exchange rather than serve a dead token.
        var cache = CreateCache();
        var key = NewKey();

        await cache.SetAsync(key, "should-not-be-cached", TimeSpan.Zero);
        Assert.Null(await cache.GetAsync(key));

        await cache.SetAsync(key, "should-not-be-cached", TimeSpan.FromSeconds(-1));
        Assert.Null(await cache.GetAsync(key));
    }

    [Fact]
    public async Task DistinctCacheKeys_DoNotCollide()
    {
        var cache = CreateCache();
        var keyA = NewKey();
        var keyB = NewKey();

        await cache.SetAsync(keyA, "token-a", TimeSpan.FromMinutes(5));
        await cache.SetAsync(keyB, "token-b", TimeSpan.FromMinutes(5));

        Assert.Equal("token-a", await cache.GetAsync(keyA));
        Assert.Equal("token-b", await cache.GetAsync(keyB));
    }

    // ── GetOrCreateAsync — cache-stampede protection (API_HARDENING_CHECKLIST.md Tier 2) ────────

    [Fact]
    public async Task GetOrCreateAsync_CacheHit_NeverInvokesExchange()
    {
        var cache = CreateCache();
        var key = NewKey();
        await cache.SetAsync(key, "already-cached", TimeSpan.FromMinutes(5));

        var invoked = false;
        var result = await cache.GetOrCreateAsync(key, _ =>
        {
            invoked = true;
            return Task.FromResult<(string, TimeSpan)?>(("should-not-happen", TimeSpan.FromMinutes(5)));
        }, CancellationToken.None);

        Assert.Equal("already-cached", result);
        Assert.False(invoked);
    }

    [Fact]
    public async Task GetOrCreateAsync_CacheMiss_InvokesExchangeOnce_PersistsResult()
    {
        var cache = CreateCache();
        var key = NewKey();

        var result = await cache.GetOrCreateAsync(
            key, _ => Task.FromResult<(string, TimeSpan)?>(("new-token", TimeSpan.FromMinutes(5))), CancellationToken.None);

        Assert.Equal("new-token", result);
        Assert.Equal("new-token", await cache.GetAsync(key)); // actually persisted to Redis, not just returned in-memory
    }

    [Fact]
    public async Task GetOrCreateAsync_ExchangeReturnsNull_ReturnsNull_CachesNothing()
    {
        var cache = CreateCache();
        var key = NewKey();

        var result = await cache.GetOrCreateAsync(key, _ => Task.FromResult<(string, TimeSpan)?>(null), CancellationToken.None);

        Assert.Null(result);
        Assert.Null(await cache.GetAsync(key));
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentMissesForSameKey_ExchangeInvokedOnlyOnce()
    {
        // The actual stampede scenario: N concurrent callers all miss the cache for the same key
        // at once (e.g. right after the previous token expired). Only one should actually hit the
        // vendor's token endpoint; everyone else should get that same result.
        var cache = CreateCache();
        var key = NewKey();
        var exchangeCount = 0;

        Task<(string, TimeSpan)?> Exchange(CancellationToken ct)
        {
            Interlocked.Increment(ref exchangeCount);
            return SlowExchange(ct);
        }
        static async Task<(string, TimeSpan)?> SlowExchange(CancellationToken ct)
        {
            await Task.Delay(300, ct); // simulate a real vendor round trip
            return ("shared-token", TimeSpan.FromMinutes(5));
        }

        var tasks = Enumerable.Range(0, 10).Select(_ => cache.GetOrCreateAsync(key, Exchange, CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("shared-token", r));
        Assert.Equal(1, exchangeCount);
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentMissesForDifferentKeys_ExchangeBothIndependently_NotSerialized()
    {
        var cache = CreateCache();
        var keyA = NewKey();
        var keyB = NewKey();
        var countA = 0;
        var countB = 0;

        var taskA = cache.GetOrCreateAsync(keyA, async ct =>
        {
            Interlocked.Increment(ref countA);
            await Task.Delay(100, ct);
            return ("token-a", TimeSpan.FromMinutes(5));
        }, CancellationToken.None);
        var taskB = cache.GetOrCreateAsync(keyB, async ct =>
        {
            Interlocked.Increment(ref countB);
            await Task.Delay(100, ct);
            return ("token-b", TimeSpan.FromMinutes(5));
        }, CancellationToken.None);

        var results = await Task.WhenAll(taskA, taskB);

        Assert.Equal("token-a", results[0]);
        Assert.Equal("token-b", results[1]);
        Assert.Equal(1, countA);
        Assert.Equal(1, countB);
    }

    [Fact]
    public async Task GetOrCreateAsync_LockHolderStuck_WaiterFallsBackAfterWaitBudget()
    {
        var key = NewKey();
        // "oauth2token:lock:{key}" mirrors RedisOAuth2TokenCache's private LockKey format —
        // simulates an external holder (a stuck/crashed process) that took the lock and never
        // releases it within this test's lifetime.
        var lockKey = $"oauth2token:lock:{key}";
        var db = fixture.Connection.GetDatabase();
        var stuckLockToken = Guid.NewGuid().ToString("N");
        Assert.True(await db.LockTakeAsync(lockKey, stuckLockToken, TimeSpan.FromSeconds(5)));

        // Small wait budget/poll interval so this test doesn't need to wait out the production
        // default (8s) to prove the fallback path works.
        var cache = new RedisOAuth2TokenCache(
            fixture.Connection, waitBudget: TimeSpan.FromMilliseconds(400), pollInterval: TimeSpan.FromMilliseconds(50));
        var exchangeCount = 0;

        var token = await cache.GetOrCreateAsync(key, _ =>
        {
            Interlocked.Increment(ref exchangeCount);
            return Task.FromResult<(string, TimeSpan)?>(("fallback-token", TimeSpan.FromMinutes(5)));
        }, CancellationToken.None);

        Assert.Equal("fallback-token", token);
        Assert.Equal(1, exchangeCount); // never saw the stuck holder's result, so it ran its own exchange instead of blocking forever
    }
}
