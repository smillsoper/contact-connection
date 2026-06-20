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
    Task ReceiveIncomingCall(string callRecordId, string callerNumber, string callerName);
}
