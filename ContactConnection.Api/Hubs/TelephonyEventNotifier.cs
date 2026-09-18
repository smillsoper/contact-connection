using ContactConnection.Application.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Hubs;

/// <summary>
/// ITelephonyEventNotifier implementation — wraps IHubContext to push a trigger_telephony_event
/// branch's completion to the triggering agent's own connection (group "agent:{agentId}", same
/// convention as SecureCollectNotifier). Registered in Program.cs after AddSignalR so the Hub types
/// are available. Infrastructure never references this class directly — it depends on
/// ITelephonyEventNotifier only.
/// </summary>
public class TelephonyEventNotifier(IHubContext<FlowHub, IFlowHubClient> hubContext) : ITelephonyEventNotifier
{
    public Task NotifyEndedAsync(
        Guid agentId, Guid callRecordId, string eventName, string outcome, CancellationToken ct = default) =>
        hubContext.Clients.Group($"agent:{agentId}")
            .ReceiveTelephonyEventEnded(callRecordId.ToString(), eventName, outcome);
}
