using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.AspNetCore.SignalR;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Puts an agent into after-call work when their call ends, and back to Available when it expires.
/// Shared by the normal hang-up path and supervisor Take Over — Take Over used to start the ACW
/// countdown without the return-to-Available timer, leaving the agent stuck in ACW (S169).
/// ACW of 0 seconds means no wrap-up: the agent goes straight to Unavailable, as before.
/// </summary>
public static class AfterCallWork
{
    public static async Task StartAsync(
        IAgentStateStore states, IHubContext<FlowHub, IFlowHubClient> hub,
        Guid tenantId, Guid agentId, string tenantSchemaName, int acwSeconds, CancellationToken ct)
    {
        if (acwSeconds <= 0)
        {
            await states.SetAsync(tenantId, agentId, tenantSchemaName,
                new AgentStateEntry(AgentStateCodes.Unavailable, "Unavailable", null, DateTimeOffset.UtcNow), ct);
            await hub.Clients.Group($"agent:{agentId}").ReceiveAgentStateChange(AgentStateCodes.Unavailable, "Unavailable", null);
            return;
        }

        var acw = new AgentStateEntry(AgentStateCodes.Acw, "After Call Work", null, DateTimeOffset.UtcNow);
        await states.SetAsync(tenantId, agentId, tenantSchemaName, acw, ct);
        await hub.Clients.Group($"agent:{agentId}")
            .ReceiveAgentStateChange(AgentStateCodes.Acw, "After Call Work", acw.SetAt.AddSeconds(acwSeconds).ToString("O"));

        // Fire-and-forget (both dependencies are singletons). Only returns the agent to Available if
        // they're still in THIS ACW — not if they changed state, or a later call started a new ACW.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(acwSeconds));
            var current = await states.GetAsync(tenantId, agentId);
            if (current?.Code != AgentStateCodes.Acw
                || Math.Abs((current.SetAt - acw.SetAt).TotalMilliseconds) > 1) return;
            await states.SetAsync(tenantId, agentId, tenantSchemaName,
                new AgentStateEntry(AgentStateCodes.Available, "Available", null, DateTimeOffset.UtcNow));
            await hub.Clients.Group($"agent:{agentId}").ReceiveAgentStateChange(AgentStateCodes.Available, "Available", null);
        });
    }
}
