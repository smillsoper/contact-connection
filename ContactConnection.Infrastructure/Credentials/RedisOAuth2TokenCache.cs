using ContactConnection.Application.Interfaces.Services;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Credentials;

/// <summary>Redis-backed IOAuth2TokenCache — same IConnectionMultiplexer already shared across
/// the app (SignalR backplane, call sessions, flow state), just a different key namespace.
/// lockTtl/waitBudget/pollInterval default to production-sane values; the optional constructor
/// parameters exist so tests can use tiny values to exercise the stampede-protection timing paths
/// (see RedisOAuth2TokenCacheTests) without actually waiting seconds.</summary>
internal class RedisOAuth2TokenCache(
    IConnectionMultiplexer redis,
    TimeSpan? lockTtl = null,
    TimeSpan? waitBudget = null,
    TimeSpan? pollInterval = null) : IOAuth2TokenCache
{
    // Bounds how long a single exchange can hold the lock — if the process holding it dies or
    // hangs, the lock self-expires rather than wedging every other caller for this key forever.
    private readonly TimeSpan _lockTtl = lockTtl ?? TimeSpan.FromSeconds(10);

    // How long a non-lock-holder waits (polling) for the lock holder's result before giving up
    // and running the exchange itself as a fallback.
    private readonly TimeSpan _waitBudget = waitBudget ?? TimeSpan.FromSeconds(8);
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(150);

    private static string Key(string cacheKey) => $"oauth2token:{cacheKey}";
    private static string LockKey(string cacheKey) => $"oauth2token:lock:{cacheKey}";

    public async Task<string?> GetAsync(string cacheKey, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(Key(cacheKey));
        return value.IsNullOrEmpty ? null : (string)value!;
    }

    public async Task SetAsync(string cacheKey, string token, TimeSpan ttl, CancellationToken ct = default)
    {
        if (ttl <= TimeSpan.Zero) return;
        var db = redis.GetDatabase();
        await db.StringSetAsync(Key(cacheKey), token, ttl);
    }

    public async Task<string?> GetOrCreateAsync(
        string cacheKey,
        Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>> exchange,
        CancellationToken ct = default)
    {
        var existing = await GetAsync(cacheKey, ct);
        if (existing is not null) return existing;

        var db = redis.GetDatabase();
        var lockKey = LockKey(cacheKey);
        var lockToken = Guid.NewGuid().ToString("N");
        var acquired = await db.LockTakeAsync(lockKey, lockToken, _lockTtl);

        if (acquired)
        {
            try
            {
                // Someone else may have populated the cache between our first GetAsync above and
                // actually acquiring the lock.
                var recheck = await GetAsync(cacheKey, ct);
                if (recheck is not null) return recheck;

                return await RunExchangeAndCacheAsync(cacheKey, exchange, ct);
            }
            finally
            {
                await db.LockReleaseAsync(lockKey, lockToken);
            }
        }

        // Another caller (this process or another instance) holds the lock and is exchanging
        // right now — wait for their result instead of also hitting the vendor's token endpoint.
        var deadline = DateTime.UtcNow + _waitBudget;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(_pollInterval, ct);
            var polled = await GetAsync(cacheKey, ct);
            if (polled is not null) return polled;
        }

        // The lock holder hasn't finished within the wait budget — never block indefinitely.
        // Not de-duplicated against the original holder in this edge case, but bounded.
        return await RunExchangeAndCacheAsync(cacheKey, exchange, ct);
    }

    private async Task<string?> RunExchangeAndCacheAsync(
        string cacheKey, Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>> exchange, CancellationToken ct)
    {
        var result = await exchange(ct);
        if (result is null) return null;

        await SetAsync(cacheKey, result.Value.Token, result.Value.Ttl, ct);
        return result.Value.Token;
    }
}
