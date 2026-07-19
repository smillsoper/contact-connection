using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Read-only data feeds for supervisor dashboard widgets. Each endpoint accepts the same
/// optional campaignId/clientId/groupId filter — exactly one (or none, meaning "all agents
/// in the tenant") is expected to be set by the widget's config.
/// </summary>
public static class DashboardWidgetsEndpoints
{
    public static void MapDashboardWidgetsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboard-widgets").RequireAuthorization();

        group.MapGet("/agent-state-counter", async (
            Guid? campaignId,
            Guid? clientId,
            Guid? groupId,
            bool? loggedInOnly,
            ICampaignRepository campaigns,
            IAgentGroupRepository agentGroups,
            IAgentRepository agents,
            IAgentStateStore stateStore,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            var tenantId = tenantContext.Current.Id;

            var agentIds = await ResolveAgentIdsAsync(campaignId, clientId, groupId, campaigns, agentGroups, agents, ct);

            var counts = AllStateCodes.ToDictionary(code => code, _ => 0);
            var total = 0;
            foreach (var agentId in agentIds)
            {
                var state = await stateStore.GetAsync(tenantId, agentId, ct);
                var code = state?.Code ?? AgentStateCodes.LoggedOut;
                if (loggedInOnly == true && code == AgentStateCodes.LoggedOut) continue;
                counts[code] = counts.GetValueOrDefault(code) + 1;
                total++;
            }

            return Results.Ok(new { total, by_state = counts });
        });

        group.MapGet("/agent-list", async (
            Guid? campaignId,
            Guid? clientId,
            Guid? groupId,
            bool? loggedInOnly,
            ICampaignRepository campaigns,
            IAgentGroupRepository agentGroups,
            IAgentRepository agents,
            IAgentStateStore stateStore,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            var tenantId = tenantContext.Current.Id;

            var agentIds = await ResolveAgentIdsAsync(campaignId, clientId, groupId, campaigns, agentGroups, agents, ct);

            var result = new List<object>();
            foreach (var agentId in agentIds)
            {
                var agent = await agents.GetByIdAsync(agentId, ct);
                if (agent is null || !agent.IsActive) continue;

                var state = await stateStore.GetAsync(tenantId, agentId, ct);
                var code = state?.Code ?? AgentStateCodes.LoggedOut;
                if (loggedInOnly == true && code == AgentStateCodes.LoggedOut) continue;

                result.Add(new
                {
                    agent_id    = agent.Id,
                    name        = $"{agent.FirstName} {agent.LastName}",
                    state_code  = code,
                    state_label = state?.Label ?? "Logged Out",
                    since       = state?.SetAt,
                });
            }

            return Results.Ok(result);
        });
    }

    private static readonly string[] AllStateCodes =
    [
        AgentStateCodes.Available,
        AgentStateCodes.Unavailable,
        AgentStateCodes.UnavailableBreak,
        AgentStateCodes.UnavailableLunch,
        AgentStateCodes.OnCall,
        AgentStateCodes.Acw,
        AgentStateCodes.LoggedOut,
    ];

    private static async Task<List<Guid>> ResolveAgentIdsAsync(
        Guid? campaignId,
        Guid? clientId,
        Guid? groupId,
        ICampaignRepository campaigns,
        IAgentGroupRepository agentGroups,
        IAgentRepository agents,
        CancellationToken ct)
    {
        var ids = new HashSet<Guid>();

        if (campaignId.HasValue)
        {
            await AddCampaignAgentsAsync(campaignId.Value, ids, campaigns, agentGroups, ct);
        }
        else if (clientId.HasValue)
        {
            var clientCampaigns = await campaigns.GetAllAsync(clientId, ct);
            foreach (var c in clientCampaigns)
                await AddCampaignAgentsAsync(c.Id, ids, campaigns, agentGroups, ct);
        }
        else if (groupId.HasValue)
        {
            var grp = await agentGroups.GetByIdWithMembersAsync(groupId.Value, ct);
            if (grp is not null)
                foreach (var m in grp.Members) ids.Add(m.AgentId);
        }
        else
        {
            var all = await agents.GetAllAsync(ct);
            foreach (var a in all.Where(a => a.IsActive)) ids.Add(a.Id);
        }

        return ids.ToList();
    }

    private static async Task AddCampaignAgentsAsync(
        Guid campaignId,
        HashSet<Guid> ids,
        ICampaignRepository campaigns,
        IAgentGroupRepository agentGroups,
        CancellationToken ct)
    {
        var campaign = await campaigns.GetByIdWithAssignmentsAsync(campaignId, ct);
        if (campaign is null) return;

        foreach (var a in campaign.AgentAssignments.Where(a => a.IsActive))
            ids.Add(a.AgentId);

        foreach (var g in campaign.GroupAssignments.Where(g => g.IsActive))
        {
            var grp = await agentGroups.GetByIdWithMembersAsync(g.GroupId, ct);
            if (grp is not null)
                foreach (var m in grp.Members) ids.Add(m.AgentId);
        }
    }
}
