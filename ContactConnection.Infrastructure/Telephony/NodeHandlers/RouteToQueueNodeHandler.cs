using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class RouteToQueueNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_route_to_queue";

    private readonly ITenantDbContextFactory _factory;
    private readonly IAgentStateStore _stateStore;

    public RouteToQueueNodeHandler(ITenantDbContextFactory factory, IAgentStateStore stateStore)
    {
        _factory    = factory;
        _stateStore = stateStore;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var directExtension = node["agentExtension"]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(directExtension))
        {
            // Direct bridge to a specific agent extension
            await ctx.Esl.BridgeToAgentAsync(ctx.ChannelUuid, directExtension, ctx.TenantSubdomain, ctx.CallerNumber, ct);
            return new TelephonyNodeResult(null, "bridged");
        }

        // Queue mode: push a screen pop to all agents assigned to this campaign.
        // The call stays parked in FreeSWITCH; agents see it as an incoming call
        // and can answer via the existing screen-pop flow.
        await using var db = _factory.Create(ctx.TenantSchemaName);

        // Collect all directly assigned active agents
        var agentIds = await db.AgentCampaignAssignments
            .Where(a => a.CampaignId == ctx.CampaignId && a.IsActive)
            .Select(a => a.AgentId)
            .ToListAsync(ct);

        // Add agents from active group assignments
        var groupIds = await db.GroupCampaignAssignments
            .Where(g => g.CampaignId == ctx.CampaignId)
            .Select(g => g.GroupId)
            .ToListAsync(ct);

        if (groupIds.Count > 0)
        {
            var groupAgentIds = await db.AgentGroupMembers
                .Where(m => groupIds.Contains(m.GroupId))
                .Select(m => m.AgentId)
                .ToListAsync(ct);
            agentIds.AddRange(groupAgentIds);
        }

        // Filter to agents who are currently in Available state.
        // Agents who have never set a state (null) default to Unavailable at login,
        // so they are excluded until they explicitly go Available.
        var availableAgentIds = new List<Guid>();
        foreach (var agentId in agentIds.Distinct())
        {
            var state = await _stateStore.GetAsync(ctx.TenantId, agentId, ct);
            if (state?.Code == AgentStateCodes.Available)
                availableAgentIds.Add(agentId);
        }

        // Store the eligible agent IDs so the caller's CHANNEL_HANGUP can clean up,
        // and so the screen pop is targeted.
        ctx.Vars["_queued"] = "true";
        ctx.Vars["_eligible_agents"] = string.Join(",", availableAgentIds);

        // Allow chaining — e.g. RouteToQueue → Play (hold music) — by reading the default transition.
        // Returns null if no transition is defined (old behavior, call stays parked silently).
        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, "queued");
    }
}
