using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Telephony;

public class RedisCallSessionStore : ITelephonyCallSessionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(4);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionMultiplexer _redis;

    public RedisCallSessionStore(IConnectionMultiplexer redis) => _redis = redis;

    private static string Key(string channelUuid) => $"telephony:session:{channelUuid}";

    public async Task SaveAsync(TelephonyCallSession session, CancellationToken ct = default)
    {
        var db   = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(session, JsonOpts);
        await db.StringSetAsync(Key(session.ChannelUuid), json, Ttl);
    }

    public async Task<TelephonyCallSession?> GetAsync(string channelUuid, CancellationToken ct = default)
    {
        var db   = _redis.GetDatabase();
        var json = await db.StringGetAsync(Key(channelUuid));
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<TelephonyCallSession>((string)json!, JsonOpts);
    }

    public async Task DeleteAsync(string channelUuid, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(Key(channelUuid));
    }

    public async Task SetKeyAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, value, ttl);
    }

    public async Task<string?> GetKeyAsync(string key, CancellationToken ct = default)
    {
        var db  = _redis.GetDatabase();
        var val = await db.StringGetAsync(key);
        return val.IsNullOrEmpty ? null : (string?)val;
    }

    public async Task DeleteKeyAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
    }
}
