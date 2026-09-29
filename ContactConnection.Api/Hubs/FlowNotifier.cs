using ContactConnection.Application.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Hubs;

/// <summary>
/// IFlowNotifier implementation — wraps IHubContext to push node state to agent UI.
/// Registered in Program.cs after AddSignalR so the Hub types are available.
/// Infrastructure never references this class directly — it depends on IFlowNotifier only.
/// </summary>
public class FlowNotifier(IHubContext<FlowHub, IFlowHubClient> hubContext) : IFlowNotifier
{
    public Task PushNodeStateAsync(Guid sessionId, FlowNodeState state, CancellationToken ct = default) =>
        hubContext.Clients.Group($"session:{sessionId}").ReceiveNodeState(state);

    public Task PushSessionUpdatedAsync(Guid sessionId, FlowNodeState state, string message, CancellationToken ct = default) =>
        hubContext.Clients.Group($"session:{sessionId}").ReceiveSessionUpdated(state, message);

    public Task PushAgentSessionsChangedAsync(Guid tenantId, Guid agentId, CancellationToken ct = default) =>
        hubContext.Clients.Group($"supervisor:{tenantId}").ReceiveAgentSessionsChanged(agentId.ToString());

    public Task PushCallChangedAsync(Guid callRecordId, CancellationToken ct = default) =>
        hubContext.Clients.Group($"call:{callRecordId}").ReceiveCallChanged(callRecordId.ToString());

    public Task PushErrorAsync(Guid sessionId, string message, CancellationToken ct = default) =>
        hubContext.Clients.Group($"session:{sessionId}").ReceiveError(message);
}
