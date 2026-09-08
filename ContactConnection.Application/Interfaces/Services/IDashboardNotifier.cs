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

    /// <summary>
    /// An agent's SIP softphone registered or unregistered with FreeSWITCH — pushed to the
    /// tenant's supervisor dashboards so the "phone actually there" signal stays live without
    /// polling. Separate from <see cref="NotifyAgentStateChangedAsync"/> (agent status).
    /// </summary>
    Task NotifyAgentRegistrationChangedAsync(
        Guid tenantId,
        Guid agentId,
        bool registered,
        DateTimeOffset? since,
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

    /// <summary>
    /// A scheduled callback changed state (attempted / expired / abandoned / connected / cancelled
    /// / rescheduled). Pushed to the tenant's supervisor dashboards so the Callbacks widget's
    /// "Scheduled" list refreshes without leaning on an incidental agent- or call-state event —
    /// most of these transitions happen in the Worker's due-scan tick with no coincident push.
    /// <paramref name="campaignId"/> may be <see cref="Guid.Empty"/> when the change isn't
    /// campaign-scoped (the widget then always treats it as relevant).
    /// </summary>
    Task NotifyScheduledCallbackChangedAsync(
        Guid tenantId,
        Guid campaignId,
        string change,
        CancellationToken ct = default);
}
