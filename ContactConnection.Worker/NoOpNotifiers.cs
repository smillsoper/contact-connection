using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;

namespace ContactConnection.Worker;

/// <summary>
/// No-op stand-ins for the SignalR-backed notifier interfaces and <see cref="IEslCommanderFactory"/>
/// that <c>ContactConnection.Api</c>'s Program.cs registers (real implementations there hold
/// <c>IHubContext&lt;...&gt;</c> / open an ESL socket). The Worker host has no hubs and never
/// resolves these methods on any of its own code paths — see project memory
/// <c>project_worker_dev_boot</c> — but <c>AddInfrastructure()</c> registers services
/// (FlowEngine, TelephonyFlowEngine, AgentStateStore, CallStateHistoryRecorder, CallTraceRecorder,
/// CallRecordingController) that take these interfaces as constructor dependencies. Without *some*
/// registration, <c>Host.CreateApplicationBuilder</c>'s <c>ValidateOnBuild</c> (on by default in
/// Development) fails at startup even though nothing would ever call them.
///
/// If a Worker code path ever does end up depending on one of these for real behavior (not just
/// build-time constructibility), give it a genuine implementation instead of routing through here.
/// </summary>
internal sealed class NoOpFlowNotifier : IFlowNotifier
{
    public Task PushNodeStateAsync(Guid sessionId, FlowNodeState state, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task PushErrorAsync(Guid sessionId, string message, CancellationToken ct = default) =>
        Task.CompletedTask;
}

internal sealed class NoOpDashboardNotifier : IDashboardNotifier
{
    public Task NotifyAgentStateChangedAsync(
        Guid tenantId, Guid agentId, string stateCode, string label, DateTimeOffset since,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task NotifyCallStateChangedAsync(
        Guid tenantId, Guid campaignId, string state, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task NotifyVoicemailReceivedAsync(
        Guid tenantId, Guid campaignId, Guid voicemailId, Guid callRecordId, string? callerId,
        int durationSeconds, DateTimeOffset createdAt, CancellationToken ct = default) =>
        Task.CompletedTask;
}

internal sealed class NoOpCallTraceNotifier : ICallTraceNotifier
{
    public Task NotifyCallMatchedAsync(Guid subscriptionId, Guid callRecordId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task NotifyTraceStepAsync(Guid subscriptionId, CallTraceStepDto step, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task NotifyCallEndedAsync(Guid subscriptionId, Guid callRecordId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task NotifyTraceStoppedAsync(Guid subscriptionId, string reason, CancellationToken ct = default) =>
        Task.CompletedTask;
}

/// <summary>
/// Throws if actually invoked — unlike the notifiers above, a silent no-op here would hide a
/// real bug (a caller expecting a working ESL connection back). Nothing on the Worker's own
/// hosted-service code paths calls <see cref="IEslCommanderFactory"/> today (only
/// <c>CallRecordingController</c>'s force-unmask watchdog does, and that only fires for masks
/// started via the Api's ESL path); this registration exists solely to satisfy
/// <c>ValidateOnBuild</c>.
/// </summary>
internal sealed class NoOpEslCommanderFactory : IEslCommanderFactory
{
    public Task<IOwnedEslCommander> CreateAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IEslCommanderFactory has no real implementation in the Worker host. " +
            "If a Worker code path now needs a live ESL connection, give it a genuine " +
            "implementation instead of routing through NoOpEslCommanderFactory.");
}
