namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Pushes live updates to any open supervisor dashboards for a tenant (the "supervisor:{tenantId}"
/// SignalR group). Called by AgentStateStore after every agent state transition, and by
/// CallStateHistoryRecorder after every call queue/routing state transition.
/// </summary>
public interface IDashboardNotifier
{
    Task NotifyAgentStateChangedAsync(
        Guid tenantId,
        Guid agentId,
        string stateCode,
        string label,
        DateTimeOffset since,
        CancellationToken ct = default);

    Task NotifyCallStateChangedAsync(
        Guid tenantId,
        Guid campaignId,
        string state,
        CancellationToken ct = default);
}
