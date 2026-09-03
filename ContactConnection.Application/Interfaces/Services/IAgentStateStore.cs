namespace ContactConnection.Application.Interfaces.Services;

public static class AgentStateCodes
{
    public const string Unavailable      = "unavailable";
    public const string Available        = "available";
    public const string UnavailableBreak = "unavailable_break";
    public const string UnavailableLunch = "unavailable_lunch";
    public const string OnCall           = "on_call";
    public const string Acw              = "acw";

    /// <summary>
    /// A queue-callback placeholder reached this agent — they are held OUT of Available (so
    /// EligibleAgentRanker / QueuePollingService route no other call to them) while the outbound
    /// call to the caller is placed. Released back to Available on callback failure, promoted to
    /// OnCall when the caller answers and is bridged. Set/cleared only by the queue-callback
    /// delivery path, never chosen by the agent.
    /// </summary>
    public const string CallbackPending  = "callback_pending";

    /// <summary>
    /// Set explicitly on sign-out (see AgentShell.tsx handleLogout). Also used to classify an
    /// agent with no state at all (never logged in) for dashboard-widget display purposes —
    /// distinct from Unavailable, which implies the agent is logged in but chose not to work.
    /// </summary>
    public const string LoggedOut        = "logged_out";
}

public record AgentStateEntry(
    string Code,
    string Label,
    Guid? CustomCodeId,
    DateTimeOffset SetAt);

/// <summary>
/// Redis-backed store for real-time agent availability state.
/// Used by RouteToQueueNodeHandler to filter eligible agents.
/// SetAsync also persists the transition to agent_state_history (tenantSchemaName is required
/// for that write — this is a singleton with no HTTP request scope to resolve it from).
/// </summary>
public interface IAgentStateStore
{
    Task<AgentStateEntry?> GetAsync(Guid tenantId, Guid agentId, CancellationToken ct = default);
    Task SetAsync(Guid tenantId, Guid agentId, string tenantSchemaName, AgentStateEntry state, CancellationToken ct = default);
}
