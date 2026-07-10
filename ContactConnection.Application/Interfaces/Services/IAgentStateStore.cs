namespace ContactConnection.Application.Interfaces.Services;

public static class AgentStateCodes
{
    public const string Unavailable      = "unavailable";
    public const string Available        = "available";
    public const string UnavailableBreak = "unavailable_break";
    public const string UnavailableLunch = "unavailable_lunch";
    public const string OnCall           = "on_call";
    public const string Acw              = "acw";
}

public record AgentStateEntry(
    string Code,
    string Label,
    Guid? CustomCodeId,
    DateTimeOffset SetAt);

/// <summary>
/// Redis-backed store for real-time agent availability state.
/// Used by RouteToQueueNodeHandler to filter eligible agents.
/// </summary>
public interface IAgentStateStore
{
    Task<AgentStateEntry?> GetAsync(Guid tenantId, Guid agentId, CancellationToken ct = default);
    Task SetAsync(Guid tenantId, Guid agentId, AgentStateEntry state, CancellationToken ct = default);
}
