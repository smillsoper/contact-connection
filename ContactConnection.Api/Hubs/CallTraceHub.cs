using ContactConnection.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Hubs;

/// <summary>
/// Real-time hub for the call trace feature. Backed by a Redis SignalR backplane (see
/// Program.cs) so group broadcasts reach clients regardless of which API instance handles
/// a given call — matching state itself lives in Redis via ICallTraceSubscriptionRegistry.
///
/// Groups: each open trace popup joins "calltrace:{subscriptionId}" after starting a trace.
/// </summary>
[Authorize]
public class CallTraceHub : Hub<ICallTraceHubClient>
{
    public async Task JoinTrace(string subscriptionId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"calltrace:{subscriptionId}");

    public async Task LeaveTrace(string subscriptionId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"calltrace:{subscriptionId}");
}

/// <summary>
/// Typed client interface — what the hub can push to trace popups.
/// Injected into CallTraceRecorder/TelephonyFlowEngine/EslBackgroundService via ICallTraceNotifier.
/// </summary>
public interface ICallTraceHubClient
{
    /// <summary>
    /// A new call started matching this subscription's filter — client spawns a tab.
    /// subscriptionId is included because one connection can join multiple trace groups
    /// simultaneously (several trace popups open at once).
    /// </summary>
    Task ReceiveCallMatched(Guid subscriptionId, Guid callRecordId);

    /// <summary>A node step executed for a call this subscription is watching.</summary>
    Task ReceiveTraceStep(Guid subscriptionId, CallTraceStepDto step);

    /// <summary>A watched call ended — client can finalize/close that tab.</summary>
    Task ReceiveCallEnded(Guid subscriptionId, Guid callRecordId);

    /// <summary>The subscription itself stopped (cap reached or manual stop).</summary>
    Task ReceiveTraceStopped(Guid subscriptionId, string reason);
}
