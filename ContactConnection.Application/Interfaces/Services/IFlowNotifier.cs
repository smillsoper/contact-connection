namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Abstraction over SignalR push — keeps Infrastructure free of API dependencies.
/// Implemented by FlowNotifier in ContactConnection.Api, which holds IHubContext&lt;FlowHub&gt;.
/// Registered as scoped in Program.cs (after AddSignalR).
/// </summary>
public interface IFlowNotifier
{
    /// <summary>Push the current node state to the agent's SignalR connection.</summary>
    Task PushNodeStateAsync(Guid sessionId, FlowNodeState state, CancellationToken ct = default);

    /// <summary>Someone else changed this session's call (Call Records review — a supervisor
    /// correcting data or resubmitting an order): push the refreshed current node plus a short
    /// notice so the agent's screen reflects it without them doing anything.</summary>
    Task PushSessionUpdatedAsync(Guid sessionId, FlowNodeState state, string message, CancellationToken ct = default);

    /// <summary>An agent opened or finished a CRM script — tells the tenant's supervisor dashboards
    /// (Agent List widget) to refresh that agent's live-call links.</summary>
    Task PushAgentSessionsChangedAsync(Guid tenantId, Guid agentId, CancellationToken ct = default);

    /// <summary>Something on this call changed — tells any open Call Records detail page for it to
    /// refresh (group "call:{callRecordId}", see FlowHub.JoinCallReview).</summary>
    Task PushCallChangedAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>Push an error to the agent's connection.</summary>
    Task PushErrorAsync(Guid sessionId, string message, CancellationToken ct = default);
}
