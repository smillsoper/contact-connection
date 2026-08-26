using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

public class RouteToQueueNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_route_to_queue";

    private readonly ITenantDbContextFactory _factory;
    private readonly ICallStateHistoryRecorder _callStateRecorder;
    private readonly EligibleAgentRanker _ranker;

    public RouteToQueueNodeHandler(
        ITenantDbContextFactory factory, ICallStateHistoryRecorder callStateRecorder, EligibleAgentRanker ranker)
    {
        _factory           = factory;
        _callStateRecorder = callStateRecorder;
        _ranker            = ranker;
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

        // Ranked (proficiency DESC, longest-idle tie-break), currently-Available eligible agents.
        // Step 4 of the ring-strategy work will truncate this per campaign.RingStrategy; for now
        // (Step 2) every agent still gets stored, preserving today's ring-all behavior exactly —
        // this step only replaces the eligible-agent-building logic itself with the shared,
        // bug-fixed helper.
        var ranked = await _ranker.GetRankedEligibleAgentsAsync(db, ctx.TenantId, ctx.CampaignId, ct: ct);
        var availableAgentIds = ranked.Select(r => r.AgentId).ToList();

        // Store the eligible agent IDs so the caller's CHANNEL_HANGUP can clean up,
        // and so the screen pop is targeted.
        ctx.Vars["_queued"] = "true";
        ctx.Vars["_eligible_agents"] = string.Join(",", availableAgentIds);

        // Stashed so a later abandon at hangup can compute in-queue wait time without a DB round trip.
        var enteredQueueAt = DateTimeOffset.UtcNow;
        ctx.Vars["_in_queue_at"] = enteredQueueAt.ToString("O");

        await _callStateRecorder.RecordAsync(
            ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId,
            CallHistoryState.InQueue, ctx.CampaignId, agentId: null, detail: null, ct: ct);

        // Allow chaining — e.g. RouteToQueue → Play (hold music) — by reading the default transition.
        // Returns null if no transition is defined (old behavior, call stays parked silently).
        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, "queued");
    }
}
