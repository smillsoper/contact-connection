using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

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

        // Real-time count of active (non-terminal) calls per campaign, bucketed into the
        // same 4 visual states CCXOne uses: PreQueue / InQueue / WithAgent / PostAgent.
        // "routing" (agent selected, bridge not yet confirmed) folds into WithAgent — it's a
        // brief transient state and keeping a 5th bucket just for it isn't worth the extra
        // legend entry. Only campaigns with at least one active call are returned — this is a
        // "what's happening right now" widget, not a campaign roster.
        group.MapGet("/call-state-by-campaign", async (
            Guid? campaignId,
            Guid? clientId,
            ICampaignRepository campaigns,
            ICallStateHistoryRepository callStateHistory,
            TenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (tenantContext.Current is null) return Results.Unauthorized();
            var tenant = tenantContext.Current;

            List<Campaign> scopedCampaigns;
            if (campaignId.HasValue)
            {
                var c = await campaigns.GetByIdAsync(campaignId.Value, ct);
                scopedCampaigns = c is not null ? [c] : [];
            }
            else
            {
                scopedCampaigns = await campaigns.GetAllAsync(clientId, ct);
            }

            var campaignIds = scopedCampaigns.Select(c => c.Id).ToList();
            var counts = await callStateHistory.GetActiveStateCountsAsync(tenant.SchemaName, campaignIds, ct);
            var countsByCampaign = counts.ToLookup(row => row.CampaignId);

            var result = scopedCampaigns
                .OrderBy(c => c.Name)
                .Select(c =>
                {
                    var buckets = new Dictionary<string, int>
                    {
                        ["pre_queue"] = 0,
                        ["in_queue"] = 0,
                        ["with_agent"] = 0,
                        ["post_agent"] = 0,
                    };
                    foreach (var row in countsByCampaign[c.Id])
                    {
                        var bucket = row.State switch
                        {
                            CallHistoryState.PreQueue => "pre_queue",
                            CallHistoryState.InQueue => "in_queue",
                            CallHistoryState.Routing or CallHistoryState.Active => "with_agent",
                            CallHistoryState.PostAgent => "post_agent",
                            _ => null,
                        };
                        if (bucket is not null) buckets[bucket] += row.Count;
                    }

                    return new
                    {
                        campaign_id   = c.Id,
                        campaign_name = c.Name,
                        pre_queue     = buckets["pre_queue"],
                        in_queue      = buckets["in_queue"],
                        with_agent    = buckets["with_agent"],
                        post_agent    = buckets["post_agent"],
                    };
                })
                .Where(r => r.pre_queue + r.in_queue + r.with_agent + r.post_agent > 0)
                .ToList();

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
