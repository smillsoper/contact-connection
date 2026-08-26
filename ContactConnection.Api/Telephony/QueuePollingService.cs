using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Polls active call sessions every second and sends screen pops to any agents
/// that have become Available since the call was queued.
///
/// Duplicate-notification prevention uses per-agent TTL keys in Redis:
///   "queue_ring:{channelUuid}:{agentId}" with a 30-second TTL.
/// While the key exists the agent is considered "already ringing" and won't
/// receive another pop. After the TTL the call is re-offered — this gives the
/// re-ring-on-no-answer behaviour. If the agent answers, the session is deleted
/// and the orphaned ring key expires naturally.
/// </summary>
public sealed class QueuePollingService : BackgroundService
{
    private static readonly TimeSpan PollInterval  = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RingKeyTtl    = TimeSpan.FromSeconds(30);

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

        var queued = sessions.Where(s => s.Vars.GetValueOrDefault("_queued") == "true").ToList();

        if (sessions.Count > 0)
            _logger.LogInformation(
                "QueuePoller: {Total} session(s) in Redis, {Queued} queued",
                sessions.Count, queued.Count);

        if (queued.Count == 0) return;

        foreach (var session in queued)
            await NotifyNewlyAvailableAgentsAsync(session, ct);
    }

    private async Task NotifyNewlyAvailableAgentsAsync(TelephonyCallSession session, CancellationToken ct)
    {
        _logger.LogInformation(
            "QueuePoller: processing queued session {Uuid} — campaign={CampaignId} tenant={TenantId} schema={Schema}",
            session.ChannelUuid, session.CampaignId, session.TenantId, session.TenantSchemaName);

        // Ranked (proficiency DESC, longest-idle tie-break), currently-Available eligible agents
        // for this campaign (direct + active group assignments) — shared with
        // RouteToQueueNodeHandler via EligibleAgentRanker.
        using var scope = _scopeFactory.CreateScope();
        var dbFactory   = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var ranker      = scope.ServiceProvider.GetRequiredService<EligibleAgentRanker>();
        await using var db = dbFactory.Create(session.TenantSchemaName);

        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == session.CampaignId, ct);
        var ranked = await ranker.GetRankedEligibleAgentsAsync(db, session.TenantId, session.CampaignId, ct: ct);

        // RingTopNByProficiency truncates to the top N here too, so a re-poll (an agent newly
        // going Available mid-queue) still only offers the call to the same restricted set the
        // initial RouteToQueueNodeHandler pass would have. RingAll and (until the arbitration
        // work lands) AutoAnswerBestAgent still ring everyone ranked.
        var eligible = campaign?.RingStrategy == CampaignRingStrategy.RingTopNByProficiency
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

    private static string RingKey(string channelUuid, Guid agentId) =>
        $"queue_ring:{channelUuid}:{agentId}";
}
