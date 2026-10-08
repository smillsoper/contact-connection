using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Health;

/// <summary>
/// Integration call outcomes for platform health (S184 phase 2): each integration reports every call as a success or a
/// failure; the Worker's health round turns the last hour into error rates. Counted per minute in Redis so the API and
/// the Worker add to the same numbers. Never throws, never slows the caller (fire-and-forget).
/// </summary>
public interface IIntegrationHealth
{
    /// <param name="source">e.g. "api:{definitionId}", "tax:avalara", "tts:elevenlabs", "stt:elevenlabs", "email", "stripe-webhook".</param>
    void Record(string source, bool ok, string? error = null);

    /// <summary>A client API's circuit breaker just refused a call (the vendor kept failing) — shown for a minute.</summary>
    void CircuitOpen(string definitionId);
}

public sealed class RedisIntegrationHealth(IConnectionMultiplexer redis) : IIntegrationHealth
{
    private const string Prefix = "health:int:";
    private const string SourcesKey = Prefix + "sources";
    private const string CircuitPrefix = Prefix + "circuit:";

    private static string Bucket(string source, DateTimeOffset at) => $"{Prefix}{source}:{at:yyyyMMddHHmm}";
    private static string LastErrorKey(string source) => $"{Prefix}{source}:last-error";

    public void Record(string source, bool ok, string? error = null)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = Bucket(source, DateTimeOffset.UtcNow);
            db.HashIncrement(key, ok ? "ok" : "fail", 1, CommandFlags.FireAndForget);
            db.KeyExpire(key, TimeSpan.FromHours(2), CommandFlags.FireAndForget);
            db.SetAdd(SourcesKey, source, CommandFlags.FireAndForget);
            if (!ok && !string.IsNullOrWhiteSpace(error))
                db.StringSet(LastErrorKey(source), error.Length > 200 ? error[..200] : error, TimeSpan.FromHours(24), flags: CommandFlags.FireAndForget);
        }
        catch { /* health counting must never break the call it's counting */ }
    }

    public void CircuitOpen(string definitionId)
    {
        try { redis.GetDatabase().StringSet(CircuitPrefix + definitionId, "1", TimeSpan.FromSeconds(75), flags: CommandFlags.FireAndForget); }
        catch { }
    }

    /// <summary>The last hour for every source that has reported: successes, failures, last error.</summary>
    public static async Task<List<(string Source, long Ok, long Fail, string? LastError)>> ReadHourAsync(IConnectionMultiplexer redis)
    {
        var db = redis.GetDatabase();
        var now = DateTimeOffset.UtcNow;
        var result = new List<(string, long, long, string?)>();
        foreach (var member in await db.SetMembersAsync(SourcesKey))
        {
            var source = member.ToString();
            var batch = db.CreateBatch();
            var minutes = Enumerable.Range(0, 60).Select(m => batch.HashGetAllAsync(Bucket(source, now.AddMinutes(-m)))).ToList();
            var last = batch.StringGetAsync(LastErrorKey(source));
            batch.Execute();
            long ok = 0, fail = 0;
            foreach (var entries in await Task.WhenAll(minutes))
                foreach (var e in entries)
                {
                    if (e.Name == "ok") ok += (long)e.Value;
                    else if (e.Name == "fail") fail += (long)e.Value;
                }
            var lastError = await last;
            result.Add((source, ok, fail, lastError.IsNullOrEmpty ? null : lastError.ToString()));
        }
        return result;
    }

    /// <summary>Client API definitions whose circuit breaker is open right now.</summary>
    public static async Task<List<string>> OpenCircuitsAsync(IConnectionMultiplexer redis)
    {
        var open = new List<string>();
        foreach (var source in (await redis.GetDatabase().SetMembersAsync(SourcesKey)).Select(s => s.ToString()).Where(s => s.StartsWith("api:")))
        {
            var id = source[4..];
            if (await redis.GetDatabase().KeyExistsAsync(CircuitPrefix + id)) open.Add(id);
        }
        return open;
    }
}
