using ContactConnection.Application.Services;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Abstraction over SignalR push for the call trace feature — keeps Infrastructure free of
/// API dependencies. Implemented by CallTraceNotifier in ContactConnection.Api, which holds
/// IHubContext&lt;CallTraceHub&gt;. Registered as scoped in Program.cs (after AddSignalR).
/// </summary>
public interface ICallTraceNotifier
{
    /// <summary>A new call started matching this subscription's filter — client spawns a tab.</summary>
    Task NotifyCallMatchedAsync(Guid subscriptionId, Guid callRecordId, CancellationToken ct = default);

    /// <summary>A node step executed for a call this subscription is watching.</summary>
    Task NotifyTraceStepAsync(Guid subscriptionId, CallTraceStepDto step, CancellationToken ct = default);

    /// <summary>A watched call ended — client can finalize/close that tab.</summary>
    Task NotifyCallEndedAsync(Guid subscriptionId, Guid callRecordId, CancellationToken ct = default);

    /// <summary>The subscription itself stopped (cap reached or manual stop).</summary>
    Task NotifyTraceStoppedAsync(Guid subscriptionId, string reason, CancellationToken ct = default);
}
