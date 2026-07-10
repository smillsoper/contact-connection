using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Telephony;

public sealed class AgentStateStore : IAgentStateStore
{
    private readonly IConnectionMultiplexer _redis;

    public AgentStateStore(IConnectionMultiplexer redis) => _redis = redis;

    private static string Key(Guid tenantId, Guid agentId) =>
        $"agent:state:{tenantId}:{agentId}";

    public async Task<AgentStateEntry?> GetAsync(Guid tenantId, Guid agentId, CancellationToken ct = default)
    {
        var db  = _redis.GetDatabase();
        var val = await db.StringGetAsync(Key(tenantId, agentId));
        if (val.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<AgentStateEntry>((string)val!,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    public async Task SetAsync(Guid tenantId, Guid agentId, AgentStateEntry state, CancellationToken ct = default)
    {
        var db   = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(state,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await db.StringSetAsync(Key(tenantId, agentId), json, TimeSpan.FromHours(12));
    }
}
