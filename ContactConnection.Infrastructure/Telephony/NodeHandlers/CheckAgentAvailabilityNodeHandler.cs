using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_check_agent_availability — branches on whether at least one agent is both eligible for a
/// campaign (directly assigned, or via an active group assignment) AND currently Available (live
/// Redis presence) right now. Delegates to <see cref="EligibleAgentRanker"/> — the exact ranked-
/// eligible-agent query RouteToQueueNodeHandler and QueuePollingService use to decide
/// whether/who to ring — rather than a separate reimplementation.
///
/// Fixed S138 (previously a "Phase 1" stub, never finished): the old logic only checked whether
/// an AgentCampaignAssignment/GroupCampaignAssignment row existed for the campaign — it never
/// looked at live agent state at all, so this node reported "available" even when every assigned
/// agent was offline, on a call, or on a break. It also didn't filter GroupCampaignAssignment.
/// IsActive (same bug EligibleAgentRanker's own doc comment records fixing for the queue-delivery
/// path — a deactivated group assignment still counted here). Reusing the ranker fixes both at
/// once and keeps this node from drifting out of sync with the real queue loop again.
/// </summary>
public class CheckAgentAvailabilityNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_check_agent_availability";

    private readonly ITenantDbContextFactory _factory;
    private readonly EligibleAgentRanker _ranker;

    public CheckAgentAvailabilityNodeHandler(ITenantDbContextFactory factory, EligibleAgentRanker ranker)
    {
        _factory = factory;
        _ranker   = ranker;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        // Optional override: node can specify a different campaign to check
        var campaignIdOverride = node["campaignId"]?.GetValue<string>() is { Length: > 0 } s
            && Guid.TryParse(s, out var g) ? g : (Guid?)null;

        var campaignId = campaignIdOverride ?? ctx.CampaignId;

        await using var db = _factory.Create(ctx.TenantSchemaName);
        var ranked = await _ranker.GetRankedEligibleAgentsAsync(db, ctx.TenantId, campaignId, ct: ct);

        var transition = ranked.Count > 0 ? "available" : "unavailable";
        var nextNodeId = node["transitions"]?[transition]?.GetValue<string>()
                      ?? node["transitions"]?["default"]?.GetValue<string>();

        return new TelephonyNodeResult(nextNodeId, transition);
    }
}
