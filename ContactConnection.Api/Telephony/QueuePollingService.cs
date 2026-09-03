using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Polls active call sessions every second and delivers queued calls to agents.
///
/// Two delivery styles, dispatched per campaign.RingStrategy:
///
///   RingAll / RingTopNByProficiency — still click-based, unchanged from the original design:
///   sends a SignalR screen pop to every eligible (ranked, and for RingTopN, truncated) agent;
///   the agent's own "Pick Up" click claims the call via QueuedCallDeliveryService. Duplicate-
///   notification prevention uses per-agent TTL keys in Redis: "queue_ring:{channelUuid}:
///   {agentId}" with a 30-second TTL. While the key exists the agent is considered "already
///   ringing" and won't receive another pop; after the TTL the call is re-offered (re-ring-on-
///   no-answer). If the agent answers, the session is deleted and the orphaned ring key expires
///   naturally.
///
///   AutoAnswerBestAgent — server-initiated, no click: each poll tick, queued calls on this
///   strategy are processed highest-effective-priority-first (Campaign.Priority, boosted by
///   Queue Acceleration the longer a call has waited — see EffectivePriorityCalculator) so that
///   when two such calls are competing for an overlapping agent pool, the more urgent one wins.
///   For each, the top-ranked eligible agent is exclusively claimed via an atomic Redis SETNX
///   ("agent_claim:{tenantId}:{agentId}", short TTL) — the real risk this guards against is a
///   second, horizontally-scaled API instance's own QueuePollingService racing on the same
///   agent, not the single-threaded ESL socket (each delivery opens its own short-lived
///   EslClient, same as the click path always has). On delivery failure the claim releases and
///   the next-ranked candidate is tried (bounded); on success the agent is excluded from every
///   other pass for the rest of this tick, including the RingAll/RingTopN pass below, so nobody
///   gets rung for the tenant line the same instant they're being auto-connected elsewhere.
/// </summary>
public sealed class QueuePollingService : BackgroundService
{
    private static readonly TimeSpan PollInterval  = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RingKeyTtl    = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AgentClaimTtl = TimeSpan.FromSeconds(10);
    private const int MaxAutoAnswerAttempts = 3;

    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly IHubContext<FlowHub, IFlowHubClient> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueuePollingService> _logger;

    public QueuePollingService(
        ITelephonyCallSessionStore sessionStore,
        IHubContext<FlowHub, IFlowHubClient> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<QueuePollingService> logger)
    {
        _sessionStore = sessionStore;
        _hub          = hub;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await PollAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "QueuePollingService: unhandled error during poll");
            }
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var sessions = await _sessionStore.GetAllAsync(ct);

        // A queued caller currently inside an input/record sub-dialog (tf_ivr_menu → ivr_collect,
        // or tf_voicemail → vm_record) is NOT deliverable — bridging them to an agent mid-
        // play_and_get_digits races the contactconnection::ivr_done / vm_done resume transfer and
        // loses the collected digits. They keep their queue slot (and _in_queue_at is untouched,
        // so their wait clock and timeout math continue) and re-enter the deliverable set the
        // moment the sub-dialog completes. This also covers a legitimate mid-queue "press 1 for a
        // callback / to leave a message" branch of any length.
        var queued       = sessions.Where(IsDeliverable).ToList();
        var inSubDialog   = sessions.Count(s => s.Vars.GetValueOrDefault("_queued") == "true" && !IsDeliverable(s));

        // "session(s)" here = every live TelephonyCallSession in Redis: queued callers PLUS
        // calls already delivered/bridged to an agent (delivery removes _queued from the
        // session, so an in-progress call is a non-queued session — not a leak). Session 97
        // verified via per-call tracing that sessions are deleted promptly on hangup; a
        // non-queued count simply reflects active calls.
        if (sessions.Count > 0)
            _logger.LogInformation(
                "QueuePoller: {Queued} queued, {InSubDialog} in IVR/voicemail, {Active} active call(s) ({Total} session(s) in Redis)",
                queued.Count, inSubDialog, sessions.Count - queued.Count - inSubDialog, sessions.Count);

        if (queued.Count == 0) return;

        // Arbitration (which call gets which agent when campaigns share an agent pool) only
        // makes sense within one tenant — agents/campaigns are tenant-schema-scoped, so
        // cross-tenant competition for the same agent can't happen.
        foreach (var tenantGroup in queued.GroupBy(s => s.TenantId))
            await ProcessTenantQueueAsync(tenantGroup.ToList(), ct);
    }

    private async Task ProcessTenantQueueAsync(List<TelephonyCallSession> sessions, CancellationToken ct)
    {
        using var scope         = _scopeFactory.CreateScope();
        var dbFactory            = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var ranker               = scope.ServiceProvider.GetRequiredService<EligibleAgentRanker>();
        var deliveryService      = scope.ServiceProvider.GetRequiredService<QueuedCallDeliveryService>();
        var queueCallbackDelivery = scope.ServiceProvider.GetRequiredService<QueueCallbackDeliveryService>();
        var callStateRecorder    = scope.ServiceProvider.GetRequiredService<ICallStateHistoryRecorder>();
        var telephonyFlowEngine  = scope.ServiceProvider.GetRequiredService<ITelephonyFlowEngine>();
        var config               = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var tenantId     = sessions[0].TenantId;
        var tenantSchema = sessions[0].TenantSchemaName;
        await using var db = dbFactory.Create(tenantSchema);

        var campaignIds = sessions.Select(s => s.CampaignId).Distinct().ToList();
        var campaigns = await db.Campaigns
            .Where(c => campaignIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var now = DateTimeOffset.UtcNow;

        // ── QueueTimeoutSeconds eviction — before any delivery/arbitration work, so an evicted
        // call never competes for an agent it's about to be kicked out of the queue for anyway.
        var activeSessions = new List<TelephonyCallSession>();
        foreach (var session in sessions)
        {
            if (!campaigns.TryGetValue(session.CampaignId, out var campaign))
            {
                activeSessions.Add(session);
                continue;
            }

            var secondsWaited = (now - ParseInQueueAt(session, now)).TotalSeconds;
            if (campaign.QueueTimeoutSeconds > 0 && secondsWaited >= campaign.QueueTimeoutSeconds)
            {
                await EvictTimedOutCallAsync(session, tenantId, tenantSchema, callStateRecorder, telephonyFlowEngine, config, ct);
                continue;
            }

            activeSessions.Add(session);
        }

        if (activeSessions.Count == 0) return;

        // Agents claimed this tick — excluded from every later pass so nobody gets double-offered
        // (auto-answered on one call while simultaneously rung for another).
        var claimedThisTick = new HashSet<Guid>();

        // ── Queue-callback placeholders — "virtual hold" ───────────────────────────────────
        // A placeholder is a session whose caller has hung up but which keeps its queue slot
        // (_queue_callback set, no reserved agent yet). It's not bridgeable — deliver it by
        // reserving an agent and dialing the caller back. Longest-waiting first; pulled out of
        // activeSessions so the auto-answer / ring passes below never touch it.
        var placeholders = activeSessions
            .Where(s => s.Vars.GetValueOrDefault("_queue_callback") == "true"
                        && string.IsNullOrEmpty(s.Vars.GetValueOrDefault("_queue_callback_reserved_agent_id"))
                        && !RetryCoolingOff(s, now))
            .OrderBy(s => ParseInQueueAt(s, now))
            .ToList();
        if (placeholders.Count > 0)
        {
            activeSessions.RemoveAll(s => s.Vars.GetValueOrDefault("_queue_callback") == "true");
            foreach (var placeholder in placeholders)
                await TryDeliverQueueCallbackAsync(
                    placeholder, tenantId, tenantSchema, tenantSubdomain: placeholder.TenantSubdomain,
                    db, ranker, queueCallbackDelivery, claimedThisTick, ct);
        }

        // ── AutoAnswerBestAgent — highest effective priority first ──────────────────────────
        var autoAnswerCandidates = activeSessions
            .Where(s => campaigns.TryGetValue(s.CampaignId, out var c) && c.RingStrategy == CampaignRingStrategy.AutoAnswerBestAgent)
            .Select(s => (Session: s, Campaign: campaigns[s.CampaignId], InQueueAt: ParseInQueueAt(s, now)));
        var autoAnswerQueue = OrderByArbitrationPriority(autoAnswerCandidates, now);

        foreach (var (session, _, _) in autoAnswerQueue)
            await TryAutoAnswerDeliverAsync(session, tenantId, tenantSchema, db, ranker, deliveryService, claimedThisTick, ct);

        // ── RingAll / RingTopNByProficiency — click-based, unchanged mechanics ──────────────
        foreach (var session in activeSessions)
        {
            if (!campaigns.TryGetValue(session.CampaignId, out var campaign)) continue;
            if (campaign.RingStrategy == CampaignRingStrategy.AutoAnswerBestAgent) continue; // handled above

            await NotifyEligibleAgentsAsync(session, campaign, db, ranker, claimedThisTick, ct);
        }
    }

    /// <summary>A queued call that's waited past campaign.QueueTimeoutSeconds — dequeues it,
    /// records the abandon, and either resumes the flow at the "on_timeout" node
    /// RouteToQueueNodeHandler stashed (if one was wired) or hangs up directly, mirroring
    /// TelEndNodeHandler's own "no transition defined" fallback so the caller is never left
    /// silently parked.</summary>
    private async Task EvictTimedOutCallAsync(
        TelephonyCallSession session, Guid tenantId, string tenantSchema,
        ICallStateHistoryRecorder callStateRecorder, ITelephonyFlowEngine telephonyFlowEngine,
        IConfiguration config, CancellationToken ct)
    {
        session.Vars.Remove("_queued");
        await _sessionStore.SaveAsync(session, ct);

        // A queue-callback placeholder that waited out the timeout is a callback abandon, not a
        // queue-timeout abandon — and there is no live channel to resume a flow node on.
        var isQueueCallback = session.Vars.GetValueOrDefault("_queue_callback") == "true";

        await callStateRecorder.RecordAsync(
            tenantId, tenantSchema, session.CallRecordId, CallHistoryState.Abandoned, session.CampaignId,
            agentId: null, detail: isQueueCallback ? "Queue callback timed out in queue" : "Queue timeout",
            abandonType: isQueueCallback ? CallAbandonType.CallbackAbandon : CallAbandonType.QueueTimeout, ct: ct);

        if (isQueueCallback)
        {
            await _sessionStore.DeleteAsync(session.ChannelUuid, ct);
            _logger.LogInformation(
                "QueuePoller: queue-callback placeholder {Uuid} timed out in queue — abandoned",
                session.ChannelUuid);
            return;
        }

        var host = config["FreeSWITCH:Host"] ?? "127.0.0.1";
        var port = int.Parse(config["FreeSWITCH:EslPort"] ?? "8021");
        var pass = config["FreeSWITCH:EslPassword"] ?? "ClueCon";

        await using var esl = new EslClient();
        await esl.ConnectAsync(host, port, pass, ct);

        if (session.Vars.TryGetValue("_on_timeout_node_id", out var onTimeoutNodeId) && !string.IsNullOrEmpty(onTimeoutNodeId))
        {
            await telephonyFlowEngine.ResumeFromNodeAsync(session.ChannelUuid, onTimeoutNodeId, esl, ct);
        }
        else
        {
            await esl.HangupChannelAsync(session.ChannelUuid, ct);
        }

        _logger.LogInformation(
            "QueuePoller: evicted timed-out call {CallRecordId} from queue (channel {Uuid}, campaign {CampaignId})",
            session.CallRecordId, session.ChannelUuid, session.CampaignId);
    }

    /// <summary>Deliver one queue-callback placeholder: rank eligible agents, atomically claim
    /// the top candidate, and hand to QueueCallbackDeliveryService to reserve them + dial the
    /// caller back. On failure the claim is released and the next candidate is tried (bounded).
    /// If nobody is eligible the placeholder just stays put for the next tick.</summary>
    private async Task TryDeliverQueueCallbackAsync(
        TelephonyCallSession placeholder, Guid tenantId, string tenantSchema, string tenantSubdomain,
        TenantDbContext db, EligibleAgentRanker ranker, QueueCallbackDeliveryService queueCallbackDelivery,
        HashSet<Guid> claimedThisTick, CancellationToken ct)
    {
        var ranked = await ranker.GetRankedEligibleAgentsAsync(
            db, placeholder.TenantId, placeholder.CampaignId, excludeAgentIds: claimedThisTick, ct: ct);

        foreach (var candidate in ranked.Take(MaxAutoAnswerAttempts))
        {
            var claimKey = AgentClaimKey(tenantId, candidate.AgentId);
            if (!await _sessionStore.TrySetKeyAsync(claimKey, placeholder.ChannelUuid, AgentClaimTtl, ct))
                continue;

            var result = await queueCallbackDelivery.ReserveAndDialAsync(
                placeholder, tenantId, tenantSchema, tenantSubdomain, candidate.AgentId, ct);

            await _sessionStore.DeleteKeyAsync(claimKey, ct);

            if (result.Success)
            {
                claimedThisTick.Add(candidate.AgentId);
                _logger.LogInformation(
                    "QueuePoller: queue-callback placeholder {Uuid} → agent {AgentId} reserved, dialing caller",
                    placeholder.ChannelUuid, candidate.AgentId);
                return;
            }

            _logger.LogWarning(
                "QueuePoller: queue-callback reserve/dial for {Uuid} via agent {AgentId} failed: {Error} — next candidate",
                placeholder.ChannelUuid, candidate.AgentId, result.ErrorDetail);
        }
    }

    private async Task TryAutoAnswerDeliverAsync(
        TelephonyCallSession session, Guid tenantId, string tenantSchema, TenantDbContext db,
        EligibleAgentRanker ranker, QueuedCallDeliveryService deliveryService,
        HashSet<Guid> claimedThisTick, CancellationToken ct)
    {
        var ranked = await ranker.GetRankedEligibleAgentsAsync(
            db, session.TenantId, session.CampaignId, excludeAgentIds: claimedThisTick, ct: ct);

        foreach (var candidate in ranked.Take(MaxAutoAnswerAttempts))
        {
            var claimKey = AgentClaimKey(tenantId, candidate.AgentId);
            var claimed = await _sessionStore.TrySetKeyAsync(claimKey, session.ChannelUuid, AgentClaimTtl, ct);
            if (!claimed)
            {
                _logger.LogDebug(
                    "QueuePoller: auto-answer — agent {AgentId} already claimed (another instance?), trying next candidate",
                    candidate.AgentId);
                continue;
            }

            // Arm the client's auto-answer flag BEFORE originating — the whisper/bridge INVITE
            // that DeliverAsync triggers can otherwise reach the browser before a push sent only
            // after success would, defeating the "no click" premise. If delivery doesn't pan out,
            // ReceiveAutoConnectFailed below tells the client to drop the "Connecting…" state.
            await _hub.Clients.Group($"agent:{candidate.AgentId}").ReceiveAutoConnecting(
                session.CallRecordId.ToString(), session.CallerNumber, session.CallerNumber,
                session.DestinationNumber, session.CampaignId.ToString());

            var result = await deliveryService.DeliverAsync(
                tenantId, tenantSchema, session.TenantSubdomain, session.CallRecordId, candidate.AgentId, ct);

            await _sessionStore.DeleteKeyAsync(claimKey, ct);

            if (result.Success)
            {
                claimedThisTick.Add(candidate.AgentId);
                _logger.LogInformation(
                    "QueuePoller: auto-answer delivered call {CallRecordId} to agent {AgentId}",
                    session.CallRecordId, candidate.AgentId);
                return;
            }

            _logger.LogWarning(
                "QueuePoller: auto-answer delivery to agent {AgentId} failed for call {CallRecordId}: {Error} — trying next candidate",
                candidate.AgentId, session.CallRecordId, result.ErrorDetail);
            await _hub.Clients.Group($"agent:{candidate.AgentId}").ReceiveAutoConnectFailed(session.CallRecordId.ToString());
        }

        // No candidate succeeded (or none were eligible) — the call stays queued and this same
        // arbitration runs again next tick.
    }

    private async Task NotifyEligibleAgentsAsync(
        TelephonyCallSession session, Campaign campaign, TenantDbContext db, EligibleAgentRanker ranker,
        HashSet<Guid> claimedThisTick, CancellationToken ct)
    {
        _logger.LogInformation(
            "QueuePoller: processing queued session {Uuid} — campaign={CampaignId} tenant={TenantId} schema={Schema}",
            session.ChannelUuid, session.CampaignId, session.TenantId, session.TenantSchemaName);

        var ranked = await ranker.GetRankedEligibleAgentsAsync(
            db, session.TenantId, session.CampaignId, excludeAgentIds: claimedThisTick, ct: ct);

        // RingTopNByProficiency truncates to the top N here too, so a re-poll (an agent newly
        // going Available mid-queue) still only offers the call to the same restricted set the
        // initial RouteToQueueNodeHandler pass would have.
        var eligible = campaign.RingStrategy == CampaignRingStrategy.RingTopNByProficiency
            ? ranked.Take(campaign.RingTopN).ToList()
            : ranked;

        _logger.LogInformation(
            "QueuePoller: {Uuid} — {Count} ranked eligible agent(s): [{Agents}]",
            session.ChannelUuid, eligible.Count, string.Join(", ", eligible.Select(r => r.AgentId)));

        foreach (var agentId in eligible.Select(r => r.AgentId))
        {
            // Per-agent ring key — prevents duplicate pops within the TTL window.
            // After 30 seconds the key expires and the call is re-offered automatically.
            var ringKey = RingKey(session.ChannelUuid, agentId);
            var alreadyRinging = await _sessionStore.GetKeyAsync(ringKey, ct);
            if (alreadyRinging is not null)
            {
                _logger.LogDebug("QueuePoller: {Uuid} — agent {AgentId} ring key still active, skipping", session.ChannelUuid, agentId);
                continue;
            }

            await _hub.Clients
                .Group($"agent:{agentId}")
                .ReceiveIncomingCall(
                    session.CallRecordId.ToString(),
                    session.CallerNumber,
                    session.CallerNumber,
                    session.DestinationNumber,
                    session.CampaignId.ToString());

            await _sessionStore.SetKeyAsync(ringKey, "1", RingKeyTtl, ct);

            _logger.LogInformation(
                "QueuePoller: notified agent {AgentId} of queued call {CallRecordId} (channel {Uuid})",
                agentId, session.CallRecordId, session.ChannelUuid);
        }
    }

    /// <summary>The AutoAnswerBestAgent arbitration order: highest effective priority first
    /// (Campaign.Priority, boosted by Queue Acceleration the longer a call has waited), ties
    /// broken by whichever call has been waiting longest. Extracted as a pure, static step so
    /// it's testable without any DB/Redis/SignalR — see QueuePollingServiceArbitrationTests.
    /// Internal (not private) for that same reason: this project's InternalsVisibleTo already
    /// covers ContactConnection.Api.Tests.</summary>
    internal static List<(TelephonyCallSession Session, Campaign Campaign, DateTimeOffset InQueueAt)> OrderByArbitrationPriority(
        IEnumerable<(TelephonyCallSession Session, Campaign Campaign, DateTimeOffset InQueueAt)> candidates,
        DateTimeOffset now) =>
        candidates
            .OrderByDescending(x => EffectivePriorityCalculator.Compute(x.Campaign, (now - x.InQueueAt).TotalSeconds))
            .ThenBy(x => x.InQueueAt) // tie-break: longest wait first
            .ToList();

    /// <summary>A session is deliverable to an agent when it is queued AND not currently inside
    /// an input/record sub-dialog (tf_ivr_menu's ivr_collect or tf_voicemail's vm_record). The
    /// voicemail salvage state ("_vm_in_progress" == "salvaging") counts as in-progress too.
    /// Internal for QueuePollingServiceArbitrationTests (InternalsVisibleTo covers Api.Tests).</summary>
    internal static bool IsDeliverable(TelephonyCallSession s) =>
        s.Vars.GetValueOrDefault("_queued") == "true"
        && string.IsNullOrEmpty(s.Vars.GetValueOrDefault("_ivr_in_progress"))
        && string.IsNullOrEmpty(s.Vars.GetValueOrDefault("_vm_in_progress"));

    /// <summary>A queue-callback placeholder whose last dial attempt failed carries
    /// _queue_callback_retry_after — a short cool-off before it's re-dialed.</summary>
    private static bool RetryCoolingOff(TelephonyCallSession s, DateTimeOffset now) =>
        s.Vars.TryGetValue("_queue_callback_retry_after", out var iso)
        && DateTimeOffset.TryParse(iso, out var after)
        && now < after;

    private static DateTimeOffset ParseInQueueAt(TelephonyCallSession session, DateTimeOffset fallback) =>
        session.Vars.TryGetValue("_in_queue_at", out var iso) && DateTimeOffset.TryParse(iso, out var parsed)
            ? parsed
            : fallback;

    private static string RingKey(string channelUuid, Guid agentId) =>
        $"queue_ring:{channelUuid}:{agentId}";

    private static string AgentClaimKey(Guid tenantId, Guid agentId) =>
        $"agent_claim:{tenantId}:{agentId}";
}
