using ContactConnection.Application.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Hubs;

/// <summary>
/// IDashboardNotifier implementation — wraps IHubContext to push to supervisor dashboards.
/// Registered as a singleton (IHubContext is itself singleton-safe) since it's consumed by
/// AgentStateStore, which is a singleton with no HTTP request scope.
/// </summary>
public class DashboardNotifier(IHubContext<FlowHub, IFlowHubClient> hubContext) : IDashboardNotifier
{
    public Task NotifyAgentStateChangedAsync(
        Guid tenantId, Guid agentId, string stateCode, string label, DateTimeOffset since, CancellationToken ct = default) =>
        hubContext.Clients.Group($"supervisor:{tenantId}")
            .ReceiveAgentStateSnapshot(agentId.ToString(), stateCode, label, since.ToString("O"));

    public Task NotifyCallStateChangedAsync(
        Guid tenantId, Guid campaignId, string state, CancellationToken ct = default) =>
        hubContext.Clients.Group($"supervisor:{tenantId}")
            .ReceiveCallStateSnapshot(campaignId.ToString(), state);

    public Task NotifyVoicemailReceivedAsync(
        Guid tenantId, Guid campaignId, Guid voicemailId, Guid callRecordId,
        string? callerId, int durationSeconds, DateTimeOffset createdAt, CancellationToken ct = default) =>
        hubContext.Clients.Group($"supervisor:{tenantId}")
            .ReceiveVoicemail(
                voicemailId.ToString(), campaignId.ToString(), callRecordId.ToString(),
                callerId ?? "", durationSeconds, createdAt.ToString("O"));
}
