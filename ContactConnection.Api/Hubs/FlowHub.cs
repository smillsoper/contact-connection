using ContactConnection.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Hubs;

/// <summary>
/// Real-time hub for flow engine → agent UI communication.
///
/// Connection lifecycle:
///   Agent UI connects on page load with JWT Bearer token.
///   Engine calls PushNodeState() after each advance — agent UI renders whatever it receives.
///   Agent UI is a thin renderer; all logic lives in the engine.
///
/// Groups:
///   Each agent joins group "session:{sessionId}" on StartSession.
///   Node state pushes are sent to that group only.
///   Supervisor joins "supervisor:{tenantId}" to see all active sessions.
/// </summary>
[Authorize]
public class FlowHub : Hub<IFlowHubClient>
{
    /// <summary>Auto-join the agent's personal group on connect so ESL screen pops can reach them.</summary>
    public override async Task OnConnectedAsync()
    {
        var agentId = Context.User?.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(agentId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{agentId}");
        await base.OnConnectedAsync();
    }

    /// <summary>Agent calls this after starting a flow session to receive node pushes.</summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session:{sessionId}");
    }

    /// <summary>Supervisor joins to observe all active sessions for a tenant.</summary>
    public async Task JoinSupervisorView(string tenantId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"supervisor:{tenantId}");
    }
}

/// <summary>
/// Typed client interface — what the hub can push to clients.
/// Injected into FlowEngine via IHubContext&lt;FlowHub, IFlowHubClient&gt;.
/// </summary>
public interface IFlowHubClient
{
    /// <summary>Push the current node state to the agent UI after each advance.</summary>
    Task ReceiveNodeState(FlowNodeState state);

    /// <summary>Push error notification (e.g. commitment lock violation).</summary>
    Task ReceiveError(string message);

    /// <summary>ESL screen pop — inbound call parked for this agent.</summary>
    Task ReceiveIncomingCall(string callRecordId, string callerNumber, string callerName, string destinationNumber, string campaignId);

    /// <summary>Server-initiated delivery (RingStrategy.AutoAnswerBestAgent) — the system picked
    /// this agent (no click required). Pushed BEFORE the originate call, not after: the softphone
    /// must arm its auto-answer flag ahead of the whisper/bridge INVITE that follows, or JsSIP
    /// could receive that INVITE before the flag is set and fall back to a manual ring. A push
    /// this early can therefore still be followed by ReceiveAutoConnectFailed if delivery
    /// doesn't pan out (e.g. the softphone turns out to be unreachable).</summary>
    Task ReceiveAutoConnecting(string callRecordId, string callerNumber, string callerName, string destinationNumber, string campaignId);

    /// <summary>Follows a ReceiveAutoConnecting push when the delivery it preceded didn't
    /// succeed — lets the agent's UI drop the "Connecting…" state instead of getting stuck in it,
    /// since no actual call is coming for that callRecordId after all.</summary>
    Task ReceiveAutoConnectFailed(string callRecordId);

    /// <summary>
    /// A greeting/connect prompt is playing to the CALLER on <paramref name="callRecordId"/> —
    /// project_agent_connect_tone's "Playing greeting…" indicator. Only meaningful during the
    /// ReceiveAutoConnecting window (RingStrategy.AutoAnswerBestAgent), where the agent's softphone
    /// is already armed to auto-answer before the caller has actually been greeted — for a manual
    /// pick-up the agent isn't engaged with this call yet when the prompt plays, so nothing is
    /// pushed. <paramref name="playing"/> true when the prompt starts, false when it's done —
    /// see QueueCallbackDeliveryService.BridgeToReservedAgentAsync, the only current caller.
    /// </summary>
    Task ReceivePlayingGreeting(string callRecordId, bool playing);

    /// <summary>Script pop delivered after whisper bridge — pushes CRM flow session JSON to the agent.</summary>
    Task ReceiveScriptPop(string sessionJson);

    /// <summary>Server-side agent state change (e.g. on_call set at pickup, acw on hangup, available after acw).</summary>
    Task ReceiveAgentStateChange(string code, string label, string? expiresAtIso);

    /// <summary>Broadcast to supervisor dashboards — any agent's state changed (not just the receiving agent's own).</summary>
    Task ReceiveAgentStateSnapshot(string agentId, string stateCode, string label, string sinceIso);

    /// <summary>Broadcast to supervisor dashboards — a call in this campaign changed queue/routing state.</summary>
    Task ReceiveCallStateSnapshot(string campaignId, string state);

    /// <summary>Broadcast to supervisor dashboards — an agent's SIP softphone registered/unregistered
    /// with FreeSWITCH. Distinct from ReceiveAgentStateSnapshot (that's agent status).</summary>
    Task ReceiveAgentRegistrationSnapshot(string agentId, bool registered, string? sinceIso);

    /// <summary>Broadcast to supervisor dashboards — a tf_voicemail node captured a new caller message.</summary>
    Task ReceiveVoicemail(
        string voicemailId, string campaignId, string callRecordId,
        string callerId, int durationSeconds, string createdAtIso);

    /// <summary>Broadcast to supervisor dashboards — a scheduled callback changed state
    /// (attempted / expired / abandoned / connected / cancelled / rescheduled). campaignId may be
    /// empty ("00000000-…") when the change isn't campaign-scoped.</summary>
    Task ReceiveScheduledCallbackChanged(string campaignId, string change);

    /// <summary>tf_secure_collect: a capture field started or advanced on the caller's parked leg
    /// (agent is on park_with_moh). fieldKey identifies which field (e.g. "card_number") — never
    /// carries digits. fieldIndex is 0-based.</summary>
    Task ReceiveSecureCollectProgress(string callRecordId, string fieldKey, int fieldIndex, int fieldCount);

    /// <summary>tf_secure_collect: the capture finished — outcome is "collected" | "failed" |
    /// "timeout" | "caller_hung_up".</summary>
    Task ReceiveSecureCollectEnded(string callRecordId, string outcome);

    /// <summary>A CRM trigger_telephony_event branch reached its own tf_end node — eventName
    /// matches the eventName the triggering trigger_telephony_event node fired. outcome is
    /// "completed". Fires only once the branch has genuinely finished (including any
    /// set_variable/play nodes downstream), not when the branch merely starts.</summary>
    Task ReceiveTelephonyEventEnded(string callRecordId, string eventName, string outcome);
}
