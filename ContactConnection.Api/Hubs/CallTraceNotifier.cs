using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Hubs;

/// <summary>
/// ICallTraceNotifier implementation — wraps IHubContext to push to trace popups.
/// Registered in Program.cs after AddSignalR so the Hub types are available.
/// Infrastructure never references this class directly — it depends on ICallTraceNotifier only.
/// </summary>
public class CallTraceNotifier(IHubContext<CallTraceHub, ICallTraceHubClient> hubContext) : ICallTraceNotifier
{
    public Task NotifyCallMatchedAsync(Guid subscriptionId, Guid callRecordId, CancellationToken ct = default) =>
        hubContext.Clients.Group($"calltrace:{subscriptionId}").ReceiveCallMatched(subscriptionId, callRecordId);

    public Task NotifyTraceStepAsync(Guid subscriptionId, CallTraceStepDto step, CancellationToken ct = default) =>
        hubContext.Clients.Group($"calltrace:{subscriptionId}").ReceiveTraceStep(subscriptionId, step);

    public Task NotifyCallEndedAsync(Guid subscriptionId, Guid callRecordId, CancellationToken ct = default) =>
        hubContext.Clients.Group($"calltrace:{subscriptionId}").ReceiveCallEnded(subscriptionId, callRecordId);

    public Task NotifyTraceStoppedAsync(Guid subscriptionId, string reason, CancellationToken ct = default) =>
        hubContext.Clients.Group($"calltrace:{subscriptionId}").ReceiveTraceStopped(subscriptionId, reason);
}
