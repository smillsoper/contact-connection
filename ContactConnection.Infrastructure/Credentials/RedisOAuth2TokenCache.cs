using ContactConnection.Application.Interfaces.Services;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Credentials;

/// <summary>Redis-backed IOAuth2TokenCache — same IConnectionMultiplexer already shared across
/// the app (SignalR backplane, call sessions, flow state), just a different key namespace.</summary>
internal class RedisOAuth2TokenCache(IConnectionMultiplexer redis) : IOAuth2TokenCache
{
    private static string Key(string cacheKey) => $"oauth2token:{cacheKey}";

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
}
