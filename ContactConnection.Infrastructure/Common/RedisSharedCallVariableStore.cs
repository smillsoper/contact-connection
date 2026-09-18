using ContactConnection.Application.Interfaces.Services;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Common;

/// <summary>
/// Redis hash per call (one field per shared variable) — cheap to add a single key without
/// re-serializing the whole set, and reads the whole call's shared state in one round trip.
/// TTL matches FlowEngine's own session TTL (12h) rather than the shorter telephony call-session
/// TTL (4h): a CRM script session can still be open/waiting after the telephony side considers
/// the call itself finished, and should still see whatever was last shared.
/// </summary>
public class RedisSharedCallVariableStore : ISharedCallVariableStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(12);

    private readonly IConnectionMultiplexer _redis;

    public RedisSharedCallVariableStore(IConnectionMultiplexer redis) => _redis = redis;

    private static string Key(Guid callRecordId) => $"shared_vars:{callRecordId}";

    public async Task<Dictionary<string, string>> GetAllAsync(Guid callRecordId, CancellationToken ct = default)
    {
        var db      = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync(Key(callRecordId));
        var result  = new Dictionary<string, string>(entries.Length);
        foreach (var entry in entries)
            result[entry.Name!] = entry.Value!;
        return result;
    }

    public async Task SetAsync(Guid callRecordId, string key, string value, CancellationToken ct = default)
    {
        var db  = _redis.GetDatabase();
        var rkey = Key(callRecordId);
        await db.HashSetAsync(rkey, key, value);
        await db.KeyExpireAsync(rkey, Ttl);
    }
}
