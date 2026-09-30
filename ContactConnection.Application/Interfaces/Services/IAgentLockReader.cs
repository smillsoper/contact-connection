namespace ContactConnection.Application.Interfaces.Services;

/// <summary>An agent's supervisor lock, as read by enforcement points (see Agent.Lock).</summary>
public record AgentLockInfo(bool SignInLocked, string? Reason, string? ByName, DateTimeOffset LockedAt);

/// <summary>
/// Fast, cached read of an agent's supervisor lock — consulted on every agent status change
/// (AgentStateStore holds a locked agent Unavailable) and every authenticated request (a sign-in
/// lock rejects the agent's existing tokens). The Agent row is the source of truth; call
/// <see cref="Invalidate"/> after changing it so this instance sees the change immediately.
/// </summary>
public interface IAgentLockReader
{
    Task<AgentLockInfo?> GetAsync(string tenantSchemaName, Guid agentId, CancellationToken ct = default);
    void Invalidate(Guid agentId);
}
