using ContactConnection.Application.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Hubs;

/// <summary>
/// ISecureCollectNotifier implementation — wraps IHubContext to push tf_secure_collect capture
/// progress to the parked agent's own connection (group "agent:{agentId}", same convention as
/// ReceiveAgentStateChange). Registered in Program.cs after AddSignalR so the Hub types are
/// available. Infrastructure never references this class directly — it depends on
/// ISecureCollectNotifier only.
/// </summary>
public class SecureCollectNotifier(IHubContext<FlowHub, IFlowHubClient> hubContext) : ISecureCollectNotifier
{
    public Task NotifyProgressAsync(
        Guid agentId, Guid callRecordId, string fieldKey, int fieldIndex, int fieldCount,
        CancellationToken ct = default) =>
        hubContext.Clients.Group($"agent:{agentId}")
            .ReceiveSecureCollectProgress(callRecordId.ToString(), fieldKey, fieldIndex, fieldCount);

    public Task NotifyEndedAsync(Guid agentId, Guid callRecordId, string outcome, CancellationToken ct = default) =>
        hubContext.Clients.Group($"agent:{agentId}")
            .ReceiveSecureCollectEnded(callRecordId.ToString(), outcome);
}
