using ContactConnection.Application.Interfaces.Services;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>Redis-backed IOutboundRateLimiter — fixed-window counter keyed per definitionId, on
/// the same IConnectionMultiplexer already shared across the app. Fixed windows (aligned to the
/// clock, not a rolling window from each caller's first request) can allow a short burst right at
/// a window boundary — accepted, same "real protection, not perfectly precise" trade-off already
/// made elsewhere in this file's neighbors (VendorResilienceExecutor's circuit breaker sampling
/// window). windowSeconds defaults to 60 (true "per minute"); the optional constructor parameter
/// exists so tests can use a tiny window to exercise window-rollover without actually waiting.</summary>
internal class RedisOutboundRateLimiter(IConnectionMultiplexer redis, int windowSeconds = 60) : IOutboundRateLimiter
{
    public async Task<RateLimitDecision> TryAcquireAsync(Guid definitionId, int? limitPerMinute, CancellationToken ct = default)
    {
        // Unconfigured (null or non-positive) means unlimited — an existing definition's behavior
        // never changes until someone opts in, and this never touches Redis for the common case.
        if (limitPerMinute is not > 0) return RateLimitDecision.Allow;

        var db = redis.GetDatabase();
        var nowUnix     = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowStart = nowUnix / windowSeconds * windowSeconds;
        var key         = $"ratelimit:{definitionId}:{windowStart}";
        var ttl         = TimeSpan.FromSeconds(windowSeconds);

        // Seed-then-increment, in that order: SET NX EX seeds the key (count=1) with its TTL
        // atomically on whichever caller is first into this window; every subsequent caller falls
        // through to INCR on the already-TTL'd key. This ordering means the key can never end up
        // without a TTL, even if a caller dies mid-request — no risk of a permanent counter.
        var seeded = await db.StringSetAsync(key, 1, ttl, When.NotExists);
        var count  = seeded ? 1 : await db.StringIncrementAsync(key);

        if (count <= limitPerMinute.Value) return RateLimitDecision.Allow;

        var retryAfterSeconds = (int)Math.Max(1, windowStart + windowSeconds - nowUnix);
        return new RateLimitDecision(false, retryAfterSeconds);
    }
}
