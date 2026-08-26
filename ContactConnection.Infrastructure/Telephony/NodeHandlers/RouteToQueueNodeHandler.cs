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
    private readonly ITelephonyCallSessionStore _sessionStore;

    public RouteToQueueNodeHandler(
        ITenantDbContextFactory factory, ICallStateHistoryRecorder callStateRecorder, EligibleAgentRanker ranker,
        ITelephonyCallSessionStore sessionStore)
    {
        _factory           = factory;
        _callStateRecorder = callStateRecorder;
        _ranker            = ranker;
        _sessionStore       = sessionStore;
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

        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == ctx.CampaignId, ct);

        // Shared by both the MaxQueueSize-reject branch below and the normal queue-entry path —
        // an edge wired from this node's "on_timeout" handle. Also used by QueuePollingService
        // (stashed into _on_timeout_node_id below) when a queued call later exceeds
        // QueueTimeoutSeconds. Same target for both "couldn't get in" and "waited too long" —
        // operationally the same "couldn't stay queued" outcome, distinguished only by
        // CallAbandonType (QueueFull vs QueueTimeout) for reporting.
        var onTimeoutNodeId = node["transitions"]?["on_timeout"]?.GetValue<string>();

        if (campaign is not null && campaign.MaxQueueSize > 0)
        {
            var allSessions = await _sessionStore.GetAllAsync(ct);
            var currentlyQueued = allSessions.Count(
                s => s.CampaignId == ctx.CampaignId && s.Vars.GetValueOrDefault("_queued") == "true");

            if (currentlyQueued >= campaign.MaxQueueSize)
            {
                await _callStateRecorder.RecordAsync(
                    ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId,
                    CallHistoryState.Abandoned, ctx.CampaignId, agentId: null, detail: "Queue full",
                    abandonType: CallAbandonType.QueueFull, ct: ct);

                if (!string.IsNullOrEmpty(onTimeoutNodeId))
                    return new TelephonyNodeResult(onTimeoutNodeId, "queue_full");

                // No overflow path defined — never leave the caller silently parked with no audio.
                if (ctx.Esl is not null)
                    await ctx.Esl.HangupChannelAsync(ctx.ChannelUuid, ct);
                return new TelephonyNodeResult(null, "queue_full");
            }
        }

        // Ranked (proficiency DESC, longest-idle tie-break), currently-Available eligible agents.
        // RingTopNByProficiency truncates to the top N before storing — the same click-based
        // ring/claim mechanics as RingAll otherwise, just a restricted candidate set. RingAll and
        // AutoAnswerBestAgent (delivered by QueuePollingService's arbitration pass, not here)
        // both get the full ranked list.
        var ranked = await _ranker.GetRankedEligibleAgentsAsync(db, ctx.TenantId, ctx.CampaignId, ct: ct);
        var eligible = campaign?.RingStrategy == CampaignRingStrategy.RingTopNByProficiency
            ? ranked.Take(campaign.RingTopN)
            : ranked;
        var availableAgentIds = eligible.Select(r => r.AgentId).ToList();

        // Store the eligible agent IDs so the caller's CHANNEL_HANGUP can clean up,
        // and so the screen pop is targeted.
        ctx.Vars["_queued"] = "true";
        ctx.Vars["_eligible_agents"] = string.Join(",", availableAgentIds);

        // Stashed so a later abandon at hangup can compute in-queue wait time without a DB round trip.
        var enteredQueueAt = DateTimeOffset.UtcNow;
        ctx.Vars["_in_queue_at"] = enteredQueueAt.ToString("O");

        if (!string.IsNullOrEmpty(onTimeoutNodeId))
            ctx.Vars["_on_timeout_node_id"] = onTimeoutNodeId;

        await _callStateRecorder.RecordAsync(
            ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId,
            CallHistoryState.InQueue, ctx.CampaignId, agentId: null, detail: null, ct: ct);

        // Allow chaining — e.g. RouteToQueue → Play (hold music) — by reading the default transition.
        // Returns null if no transition is defined (old behavior, call stays parked silently).
        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, "queued");
    }
}
