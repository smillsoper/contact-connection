namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Abstraction over SignalR push for a CRM trigger_telephony_event branch reaching its own tf_end —
/// keeps Infrastructure free of API dependencies. Implemented by TelephonyEventNotifier in
/// ContactConnection.Api, which holds IHubContext&lt;FlowHub&gt;. Registered as scoped in
/// Program.cs (after AddSignalR).
///
/// Closes the trigger_telephony_event fire-and-continue race (see project_shared_call_variables
/// memory): a CRM script node waiting on the triggered branch's outcome needs a signal that fires
/// only once the branch has genuinely finished (including any set_variable/play nodes downstream of
/// the node that kicked the branch off), not when the branch merely starts. TelEndNodeHandler is the
/// one place that's true regardless of how many async hops (e.g. tf_play's PLAYBACK_STOP
/// continuation) the branch takes to get there.
/// </summary>
public interface ITelephonyEventNotifier
{
    /// <summary>The triggered branch reached its tf_end node — outcome is "completed".</summary>
    Task NotifyEndedAsync(
        Guid agentId, Guid callRecordId, string eventName, string outcome, CancellationToken ct = default);
}
