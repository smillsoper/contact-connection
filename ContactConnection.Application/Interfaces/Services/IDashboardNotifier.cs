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

    /// <summary>A tf_voicemail node just captured a caller message — push it to the tenant's supervisor dashboards.</summary>
    Task NotifyVoicemailReceivedAsync(
        Guid tenantId,
        Guid campaignId,
        Guid voicemailId,
        Guid callRecordId,
        string? callerId,
        int durationSeconds,
        DateTimeOffset createdAt,
        CancellationToken ct = default);
}
