using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Telephony;

public sealed class AgentStateStore : IAgentStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IAgentStateHistoryRepository _history;
    private readonly IDashboardNotifier _dashboardNotifier;
    private readonly IAgentLockReader _locks;

    /// <summary>Label a supervisor-locked agent is held at, whatever was requested.</summary>
    public const string LockedLabel = "Unavailable - Locked by supervisor";

    public AgentStateStore(IConnectionMultiplexer redis, IAgentStateHistoryRepository history, IDashboardNotifier dashboardNotifier,
        IAgentLockReader locks)
    {
        _redis             = redis;
        _history           = history;
        _dashboardNotifier = dashboardNotifier;
        _locks             = locks;
    }

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

    public async Task SetAsync(Guid tenantId, Guid agentId, string tenantSchemaName, AgentStateEntry state, CancellationToken ct = default)
    {
        // A supervisor-locked agent stays Unavailable whatever asks otherwise — the agent's own
        // picker, ACW expiry, a queue delivery releasing them. Signing out is still recorded.
        if (state.Code != AgentStateCodes.LoggedOut && await _locks.GetAsync(tenantSchemaName, agentId, ct) is not null)
            state = new AgentStateEntry(AgentStateCodes.Unavailable, LockedLabel, null, state.SetAt);

        var db   = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(state,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await db.StringSetAsync(Key(tenantId, agentId), json, TimeSpan.FromHours(12));

        var entry = AgentStateHistoryEntry.Create(
            tenantId, agentId, state.Code, state.Label, state.CustomCodeId, state.SetAt);
        await _history.AddAsync(entry, tenantSchemaName, ct);

        await _dashboardNotifier.NotifyAgentStateChangedAsync(
            tenantId, agentId, state.Code, state.Label, state.SetAt, ct);
    }
}
