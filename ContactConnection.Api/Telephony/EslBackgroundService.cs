using System.Text.Json;
using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Hosted service that maintains a persistent ESL connection to FreeSWITCH.
///
/// CHANNEL_PARK routing:
///   1. Look up Caller-Destination-Number in PhoneNumberRouting (global DID table).
///      If found → run the tenant's telephony call flow (pre-answer routing).
///   2. If not found → treat as a direct agent extension call (screen pop).
///
/// CHANNEL_HANGUP → mark CallRecord complete.
/// </summary>
public sealed class EslBackgroundService : BackgroundService
{
    private readonly IHubContext<FlowHub, IFlowHubClient> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<EslBackgroundService> _logger;
    private readonly ILogger<EslClient> _eslClientLogger;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly IAgentStateStore _stateStore;

    public EslBackgroundService(
        IHubContext<FlowHub, IFlowHubClient> hub,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<EslBackgroundService> logger,
        ILogger<EslClient> eslClientLogger,
        ITelephonyCallSessionStore sessionStore,
        IAgentStateStore stateStore)
    {
        _hub             = hub;
        _scopeFactory    = scopeFactory;
        _config          = config;
        _logger          = logger;
        _eslClientLogger = eslClientLogger;
        _sessionStore    = sessionStore;
        _stateStore      = stateStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ESL connection lost. Reconnecting in 5s.");
                await Task.Delay(5_000, stoppingToken);
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var host = _config["FreeSWITCH:Host"] ?? "127.0.0.1";
        var port = int.Parse(_config["FreeSWITCH:EslPort"] ?? "8021");
        var pass = _config["FreeSWITCH:EslPassword"] ?? "ClueCon";

        await using var esl = new EslClient(_eslClientLogger);
        await esl.ConnectAsync(host, port, pass, ct);
        // CUSTOM mod_audio_stream::* — streaming-TTS relay lifecycle (see HandleCustomEventAsync).
        // ::connect is diagnostic only; ::disconnect is the streaming-TTS equivalent of
        // PLAYBACK_STOP (resumes the flow); ::error covers vendor/relay failures so the flow
        // still advances instead of leaving the caller on a channel that will never resume.
        await esl.SubscribeAsync(
            "CHANNEL_PARK CHANNEL_HANGUP CHANNEL_HANGUP_COMPLETE CHANNEL_BRIDGE CHANNEL_UNBRIDGE PLAYBACK_STOP " +
            "CUSTOM mod_audio_stream::connect mod_audio_stream::disconnect mod_audio_stream::error", ct);

        _logger.LogInformation("ESL connected to FreeSWITCH at {Host}:{Port}", host, port);

        while (!ct.IsCancellationRequested)
        {
            var msg = await esl.ReadMessageAsync(ct);
            if (msg is null) break;

            if (msg.ContentType != "text/event-plain") continue;

            var vars = msg.ParseBody();
            if (!vars.TryGetValue("Event-Name", out var eventName)) continue;

            switch (eventName)
            {
                case "CHANNEL_PARK":
                    await HandleChannelParkAsync(vars, esl, ct);
                    break;
                case "CHANNEL_HANGUP":
                case "CHANNEL_HANGUP_COMPLETE":
                    await HandleChannelHangupAsync(vars, esl, ct);
                    break;
                case "PLAYBACK_STOP":
                    await HandlePlaybackStopAsync(vars, esl, ct);
                    break;
                case "CHANNEL_BRIDGE":
                    await HandleChannelBridgeAsync(vars, esl, ct);
                    break;
                case "CHANNEL_UNBRIDGE":
                    await HandleChannelUnbridgeAsync(vars, esl, ct);
                    break;
                case "CUSTOM":
                    await HandleCustomEventAsync(vars, esl, ct);
                    break;
            }
        }
    }

    private async Task HandleChannelParkAsync(
        Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var destination  = vars.GetValueOrDefault("Caller-Destination-Number") ?? "";
        var channelUuid  = vars.GetValueOrDefault("Unique-ID") ?? "";

        if (string.IsNullOrEmpty(destination) || string.IsNullOrEmpty(channelUuid)) return;

        // Whisper channels created by AnswerQueuedCall carry cc_whisper=true.
        // Skip them — they are internal bridge legs, not new inbound calls.
        if (vars.GetValueOrDefault("variable_cc_whisper") == "true") return;

        // Skip outbound channels — when testing with "fs_cli originate ... &park()", FreeSWITCH
        // fires CHANNEL_PARK for both the originate A-leg (outbound) and the loopback B-leg
        // (inbound). Only the inbound B-leg is the real caller; the A-leg must not be
        // processed as a second duplicate call.
        // NOTE: the actual event header is "Call-Direction", not "Channel-Call-Direction" —
        // the previous check never matched anything, so this never actually skipped the A-leg,
        // producing a duplicate CallRecord (queued, screen-popped, and answerable independently)
        // for every self-dial test call.
        if (vars.GetValueOrDefault("Call-Direction") == "outbound") return;

        var callerNumber = vars.GetValueOrDefault("Caller-Caller-ID-Number") ?? "";
        var callerName   = vars.GetValueOrDefault("Caller-Caller-ID-Name") ?? "";

        // ESL originate test calls set origination_caller_id_number but FreeSWITCH copies
        // the dialed number into Caller-Caller-ID-Number on the outbound channel. Fall back
        // to the origination variable so the correct ANI is shown even during testing.
        if (string.IsNullOrEmpty(callerNumber) || callerNumber == destination)
        {
            callerNumber = vars.GetValueOrDefault("variable_origination_caller_id_number")
                        ?? vars.GetValueOrDefault("variable_sip_from_user")
                        ?? "Unknown";
        }

        // Prevent loopback bowout: when the parked channel bridges to an agent, FreeSWITCH's
        // loopback module would otherwise tear down the loopback pair and send BYE to the agent.
        // This is a no-op on real SIP channels (variable is unknown and ignored).
        await esl.SetChannelVarAsync(channelUuid, "loopback_bowout", "false", ct);

        using var scope      = _scopeFactory.CreateScope();
        var platformDb       = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
        var dbFactory        = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var telephonyEngine  = scope.ServiceProvider.GetRequiredService<ITelephonyFlowEngine>();
        var callStateRecorder = scope.ServiceProvider.GetRequiredService<ICallStateHistoryRecorder>();

        // ── DID routing: check if destination matches a provisioned phone number ──
        // Normalize: strip leading + so "+18005551234" matches "18005551234" or "8005551234"
        var destNorm = destination.TrimStart('+');
        var routing = await platformDb.PhoneNumberRoutings
            .FirstOrDefaultAsync(r => r.IsActive && (
                r.Number == destination ||
                r.Number == destNorm ||
                "+" + r.Number == destination ||
                "1" + r.Number == destNorm), ct);

        if (routing is not null)
        {
            await HandleDidCallAsync(
                routing, callerNumber, callerName, channelUuid, destination, vars,
                esl, telephonyEngine, platformDb, dbFactory, callStateRecorder, ct);
            return;
        }

        // ── Agent extension: fall through to screen pop (existing behavior) ──
        await HandleAgentExtensionCallAsync(
            destination, callerNumber, callerName, channelUuid,
            platformDb, dbFactory, ct);
    }

    /// <summary>
    /// Inbound DID call — look up tenant + campaign, create CallRecord, run telephony flow.
    /// </summary>
    private async Task HandleDidCallAsync(
        PhoneNumberRouting routing,
        string callerNumber,
        string callerName,
        string channelUuid,
        string destinationNumber,
        Dictionary<string, string> eventVars,
        EslClient esl,
        ITelephonyFlowEngine telephonyEngine,
        ContactConnectionDbContext platformDb,
        ITenantDbContextFactory dbFactory,
        ICallStateHistoryRecorder callStateRecorder,
        CancellationToken ct)
    {
        var tenant = await platformDb.Tenants.FirstOrDefaultAsync(t => t.Id == routing.TenantId, ct);
        if (tenant is null)
        {
            _logger.LogWarning("CHANNEL_PARK DID {Uuid}: tenant {TenantId} not found", channelUuid, routing.TenantId);
            return;
        }

        await using var db = dbFactory.Create(tenant.SchemaName);

        var record = CallRecord.CreateInbound(
            tenantId: tenant.Id,
            callerId: callerNumber,
            agentId: null,
            contactIdExternal: channelUuid);

        // Stamp the campaign and dialed number so the CallRecord knows where it belongs
        record.SetCampaign(routing.CampaignId);
        record.SetDnis(routing.Number);

        db.CallRecords.Add(record);
        await db.SaveChangesAsync(ct);

        await callStateRecorder.RecordAsync(
            tenant.Id, tenant.SchemaName, record.Id,
            CallHistoryState.PreQueue, routing.CampaignId, agentId: null, detail: null, ct: ct);

        _logger.LogInformation(
            "CHANNEL_PARK DID {Uuid}: tenant={Tenant} campaign={Campaign} → CallRecord {RecordId}",
            channelUuid, tenant.Subdomain, routing.CampaignId, record.Id);

        // Extract SIP headers from event vars (variable_sip_h_* → sip_h_*) for flow handlers
        var channelVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in eventVars)
        {
            if (key.StartsWith("variable_sip_h_", StringComparison.OrdinalIgnoreCase))
                channelVars[key["variable_sip_h_".Length..]] = value;
            else if (key.StartsWith("variable_", StringComparison.OrdinalIgnoreCase))
                channelVars[key["variable_".Length..]] = value;
        }

        var ctx = new TelephonyFlowContext
        {
            ChannelUuid       = channelUuid,
            CallerNumber      = callerNumber,
            DestinationNumber = routing.Number,
            TenantId          = tenant.Id,
            CampaignId        = routing.CampaignId,
            CallRecordId      = record.Id,
            TenantSubdomain   = tenant.Subdomain,
            TenantSchemaName  = tenant.SchemaName,
            TenantTimezone    = tenant.Timezone,
            Esl               = esl,
            ChannelVars       = channelVars,
        };

        await telephonyEngine.ExecuteAsync(ctx, ct);

        // Persist the flow execution trace to the call record
        if (ctx.Trace is not null)
        {
            var traceJson = JsonSerializer.Serialize(ctx.Trace, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
            });
            record.SetTelephonyTrace(traceJson);
            await db.SaveChangesAsync(ct);
        }

        // If the flow queued the call, broadcast screen pop to agents who were already available
        // at routing time and record them in _notified_agents so the QueuePollingService
        // doesn't send duplicate notifications on the next tick.
        if (ctx.Vars.TryGetValue("_queued", out _) && ctx.Vars.TryGetValue("_eligible_agents", out var agentList))
        {
            var agentIds = agentList.Split(',', StringSplitOptions.RemoveEmptyEntries);
            _logger.LogInformation(
                "CHANNEL_PARK DID {Uuid}: notifying {Count} immediately-available agent(s): [{Agents}]",
                channelUuid, agentIds.Length, agentList);
            var notified = new List<string>();
            foreach (var agentIdStr in agentIds)
            {
                if (!Guid.TryParse(agentIdStr.Trim(), out var agentId)) continue;
                await _hub.Clients
                    .Group($"agent:{agentId}")
                    .ReceiveIncomingCall(record.Id.ToString(), callerNumber, callerName, destinationNumber, ctx.CampaignId.ToString());
                notified.Add(agentId.ToString());
            }
            // Set per-agent ring keys so the QueuePollingService doesn't duplicate within 30s
            foreach (var notifiedId in notified)
                await _sessionStore.SetKeyAsync(
                    $"queue_ring:{channelUuid}:{notifiedId}", "1", TimeSpan.FromSeconds(30), ct);
        }
        else
        {
            _logger.LogWarning(
                "CHANNEL_PARK DID {Uuid}: flow did not queue — _queued={Queued} _eligible_agents={Agents}",
                channelUuid,
                ctx.Vars.GetValueOrDefault("_queued", "(not set)"),
                ctx.Vars.GetValueOrDefault("_eligible_agents", "(not set)"));
        }
    }

    /// <summary>
    /// Direct agent extension call (agent-to-agent or originate test) — create CallRecord and push screen pop.
    /// </summary>
    private async Task HandleAgentExtensionCallAsync(
        string agentExtension,
        string callerNumber,
        string callerName,
        string channelUuid,
        ContactConnectionDbContext platformDb,
        ITenantDbContextFactory dbFactory,
        CancellationToken ct)
    {
        var tenants = await platformDb.Tenants.Where(t => t.IsActive).ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            await using var db = dbFactory.Create(tenant.SchemaName);

            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.SipExtension == agentExtension && a.IsActive, ct);

            if (agent is null) continue;

            var record = CallRecord.CreateInbound(
                tenantId: tenant.Id,
                callerId: callerNumber,
                agentId: agent.Id,
                contactIdExternal: channelUuid);

            db.CallRecords.Add(record);
            await db.SaveChangesAsync(ct);

            await _hub.Clients
                .Group($"agent:{agent.Id}")
                .ReceiveIncomingCall(record.Id.ToString(), callerNumber, callerName, agentExtension, "");

            _logger.LogInformation(
                "CHANNEL_PARK {Uuid}: agent {Ext} → CallRecord {RecordId}",
                channelUuid, agentExtension, record.Id);
            return;
        }

        _logger.LogWarning("CHANNEL_PARK {Uuid}: no agent found for extension {Ext}", channelUuid, agentExtension);
    }

    /// <summary>
    /// Resolves the live call session for a hangup/unbridge event. FreeSWITCH reports these
    /// events against whichever leg triggered them — for a bridged call that can be either the
    /// original caller/parked leg (which the session is keyed under, set at CHANNEL_PARK) or the
    /// agent's own bridge leg (e.g. the agent clicking Hang Up sends a real BYE for THEIR leg,
    /// not the caller's). Falls back to the event's Other-Leg-Unique-ID/Bridge-B-Unique-ID before
    /// giving up, so an agent-leg hangup still finds the caller-keyed session.
    /// </summary>
    private async Task<TelephonyCallSession?> ResolveSessionAsync(
        string uuid, Dictionary<string, string> vars, CancellationToken ct)
    {
        var session = await _sessionStore.GetAsync(uuid, ct);
        if (session is not null) return session;

        var otherLegUuid = vars.GetValueOrDefault("Other-Leg-Unique-ID") ?? vars.GetValueOrDefault("Bridge-B-Unique-ID");
        return string.IsNullOrEmpty(otherLegUuid) ? null : await _sessionStore.GetAsync(otherLegUuid, ct);
    }

    /// <summary>
    /// Channel hung up — fire the call_disconnected event branch (if configured), mark the
    /// CallRecord complete, and delete the live call session from Redis.
    /// </summary>
    private async Task HandleChannelHangupAsync(Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var channelUuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(channelUuid)) return;

        var cause = vars.GetValueOrDefault("Hangup-Cause") ?? "unknown";

        // Check if there is a live session for this call (DID calls always have one) — the
        // session may be keyed under this event's own uuid or its bridge partner's (see
        // ResolveSessionAsync). session.ChannelUuid (not the raw event uuid) is the correct key
        // for all session-store/CallRecord lookups below once a session is found.
        var session = await ResolveSessionAsync(channelUuid, vars, ct);

        if (session is not null)
        {
            var sessionUuid = session.ChannelUuid;

            // Read assigned agent before deleting session
            var assignedAgentIdStr = session.Vars.GetValueOrDefault("_assigned_agent_id");

            using var scope         = _scopeFactory.CreateScope();
            var telephonyEngine     = scope.ServiceProvider.GetRequiredService<ITelephonyFlowEngine>();
            var dbFactory           = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
            var traceRegistry       = scope.ServiceProvider.GetRequiredService<ICallTraceSubscriptionRegistry>();
            var traceNotifier       = scope.ServiceProvider.GetRequiredService<ICallTraceNotifier>();
            var callStateRecorder   = scope.ServiceProvider.GetRequiredService<ICallStateHistoryRecorder>();

            // Fire the call_disconnected event so the designer branch can run post-call actions
            await telephonyEngine.FireEventAsync(
                sessionUuid, "call_disconnected",
                new FireEventContext { AdditionalVars = new() { ["hangup_cause"] = cause } },
                ct);

            // Mark the call record complete
            await using var db = dbFactory.Create(session.TenantSchemaName);
            var record = await db.CallRecords.FirstOrDefaultAsync(
                r => r.ContactIdExternal == sessionUuid, ct);
            Campaign? campaign = null;
            if (record is not null)
            {
                record.Complete();
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "CHANNEL_HANGUP {Uuid} cause={Cause} → CallRecord {RecordId} completed",
                    channelUuid, cause, record.Id);

                if (session.CampaignId != Guid.Empty)
                    campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == session.CampaignId, ct);

                // Classify the call's queue-lifecycle outcome for the state history log
                if (assignedAgentIdStr is not null && Guid.TryParse(assignedAgentIdStr, out var completedAgentId))
                {
                    await callStateRecorder.RecordAsync(
                        session.TenantId, session.TenantSchemaName, record.Id,
                        CallHistoryState.Completed, session.CampaignId, completedAgentId, cause, ct: ct);
                }
                else if (session.Vars.ContainsKey("_queued"))
                {
                    var abandonLength = CallAbandonLength.Long;
                    if (session.Vars.TryGetValue("_in_queue_at", out var inQueueAtStr) &&
                        DateTimeOffset.TryParse(inQueueAtStr, out var inQueueAt))
                    {
                        var waitedSeconds = (DateTimeOffset.UtcNow - inQueueAt).TotalSeconds;
                        var threshold = campaign?.ShortAbandonThresholdSeconds ?? 10;
                        abandonLength = waitedSeconds <= threshold ? CallAbandonLength.Short : CallAbandonLength.Long;
                    }
                    await callStateRecorder.RecordAsync(
                        session.TenantId, session.TenantSchemaName, record.Id,
                        CallHistoryState.Abandoned, session.CampaignId, agentId: null, detail: cause,
                        abandonType: CallAbandonType.InQueue, abandonLength: abandonLength, ct: ct);
                }
                else
                {
                    await callStateRecorder.RecordAsync(
                        session.TenantId, session.TenantSchemaName, record.Id,
                        CallHistoryState.Abandoned, session.CampaignId, agentId: null, detail: cause,
                        abandonType: CallAbandonType.PreQueue, ct: ct);
                }

                // Let any trace popups watching this call know it ended, so they can close its tab
                var watchingSubscriptionIds = await traceRegistry.GetSubscriptionsForCallAsync(record.Id, ct);
                foreach (var subscriptionId in watchingSubscriptionIds)
                {
                    await traceRegistry.MarkCallEndedAsync(subscriptionId, record.Id, ct);
                    await traceNotifier.NotifyCallEndedAsync(subscriptionId, record.Id, ct);
                }
            }

            // Delete session — call is over
            await _sessionStore.DeleteAsync(sessionUuid, ct);

            // Safety net: the hangup event fired for one leg (possibly the agent's bridge leg,
            // not the session-keyed caller leg) — don't rely on FreeSWITCH cascading the hangup
            // to the other leg automatically (observed lingering caller legs when it didn't).
            if (!string.Equals(channelUuid, sessionUuid, StringComparison.Ordinal))
            {
                try
                {
                    await esl.HangupChannelAsync(sessionUuid, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "CHANNEL_HANGUP {Uuid}: safety-net hangup of {SessionUuid} failed (likely already gone)",
                        channelUuid, sessionUuid);
                }
            }

            // Restore agent state: ACW → available (if ACW > 0) or unavailable immediately (ACW = 0)
            if (assignedAgentIdStr is not null && Guid.TryParse(assignedAgentIdStr, out var assignedAgentId))
            {
                var acwSeconds = campaign?.AfterCallWorkSeconds ?? 0;

                if (acwSeconds > 0)
                {
                    var acwEndsAt = DateTimeOffset.UtcNow.AddSeconds(acwSeconds);
                    await _stateStore.SetAsync(session.TenantId, assignedAgentId, session.TenantSchemaName,
                        new AgentStateEntry(AgentStateCodes.Acw, "After Call Work", null, DateTimeOffset.UtcNow), ct);
                    await _hub.Clients.Group($"agent:{assignedAgentId}")
                        .ReceiveAgentStateChange(AgentStateCodes.Acw, "After Call Work", acwEndsAt.ToString("O"));
                    _logger.LogInformation(
                        "CHANNEL_HANGUP {Uuid}: agent {AgentId} → ACW for {Seconds}s", channelUuid, assignedAgentId, acwSeconds);

                    // Fire-and-forget: after ACW expires, auto-transition to available
                    // Only transitions if the agent hasn't manually changed state during ACW.
                    var tenantId         = session.TenantId;
                    var tenantSchemaName = session.TenantSchemaName;
                    var hub              = _hub;
                    var stateStore       = _stateStore;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(acwSeconds));
                        var current = await stateStore.GetAsync(tenantId, assignedAgentId);
                        if (current?.Code == AgentStateCodes.Acw)
                        {
                            await stateStore.SetAsync(tenantId, assignedAgentId, tenantSchemaName,
                                new AgentStateEntry(AgentStateCodes.Available, "Available", null, DateTimeOffset.UtcNow));
                            await hub.Clients.Group($"agent:{assignedAgentId}")
                                .ReceiveAgentStateChange(AgentStateCodes.Available, "Available", null);
                        }
                    });
                }
                else
                {
                    await _stateStore.SetAsync(session.TenantId, assignedAgentId, session.TenantSchemaName,
                        new AgentStateEntry(AgentStateCodes.Unavailable, "Unavailable", null, DateTimeOffset.UtcNow), ct);
                    await _hub.Clients.Group($"agent:{assignedAgentId}")
                        .ReceiveAgentStateChange(AgentStateCodes.Unavailable, "Unavailable", null);
                    _logger.LogInformation(
                        "CHANNEL_HANGUP {Uuid}: agent {AgentId} → unavailable (ACW=0)", channelUuid, assignedAgentId);
                }
            }

            return;
        }

        // No session: direct extension call or session already expired — fall back to tenant scan
        await HandleHangupByTenantScanAsync(channelUuid, cause, ct);
    }

    /// <summary>
    /// CHANNEL_BRIDGE fires when two channels are connected.
    /// For the whisper path: fire "agent_answer" event and push CRM script pop via SignalR.
    /// Also clears any active play loop vars on the caller's session.
    /// </summary>
    private async Task HandleChannelBridgeAsync(
        Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var uuid  = vars.GetValueOrDefault("Unique-ID") ?? "";
        var other = vars.GetValueOrDefault("Bridge-B-Unique-ID") ?? vars.GetValueOrDefault("Other-Leg-Unique-ID") ?? "";
        _logger.LogInformation("CHANNEL_BRIDGE {Uuid} ↔ {Other}", uuid, other);

        var bridgeSession = await _sessionStore.GetAsync(uuid, ct);
        if (bridgeSession is null) return;

        // Call is now bridged to an agent — record the "active" transition
        {
            using var recorderScope = _scopeFactory.CreateScope();
            var callStateRecorder   = recorderScope.ServiceProvider.GetRequiredService<ICallStateHistoryRecorder>();
            Guid? bridgedAgentId = Guid.TryParse(
                bridgeSession.Vars.GetValueOrDefault("_assigned_agent_id"), out var bridgedAgentIdParsed)
                ? bridgedAgentIdParsed : null;

            // Service Level: was this call answered within the campaign's
            // ServiceLevelThresholdSeconds of entering the queue? Reporting-only — captured here
            // for future dashboard use, no control-flow depends on it. Null when the call never
            // queued at all (e.g. a direct-extension bridge, which has no _in_queue_at).
            bool? metServiceLevel = null;
            if (bridgeSession.Vars.TryGetValue("_in_queue_at", out var inQueueAtStr) &&
                DateTimeOffset.TryParse(inQueueAtStr, out var inQueueAt))
            {
                var dbFactory = recorderScope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
                await using var db = dbFactory.Create(bridgeSession.TenantSchemaName);
                var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == bridgeSession.CampaignId, ct);
                if (campaign is not null)
                {
                    var secondsWaited = (DateTimeOffset.UtcNow - inQueueAt).TotalSeconds;
                    metServiceLevel = secondsWaited <= campaign.ServiceLevelThresholdSeconds;
                }
            }

            await callStateRecorder.RecordAsync(
                bridgeSession.TenantId, bridgeSession.TenantSchemaName, bridgeSession.CallRecordId,
                CallHistoryState.Active, bridgeSession.CampaignId, bridgedAgentId, detail: null,
                metServiceLevel: metServiceLevel, ct: ct);
        }

        // Stop any active play loop — the call is now bridged
        if (bridgeSession.Vars.ContainsKey("_play_media_arg"))
        {
            bridgeSession.Vars.Remove("_play_media_arg");
            bridgeSession.Vars.Remove("_play_loop");
            // Immediately cut the audio; without this FreeSWITCH finishes the current
            // file before the bridge audio starts coming through.
            await esl.BreakChannelAsync(uuid, ct);
        }

        // Whisper path: _pending_agent_id was stored by AnswerQueuedCall before firing agent_selected.
        // Now that the bridge is live, fire agent_answer and push the CRM script pop.
        if (bridgeSession.Vars.TryGetValue("_pending_agent_id", out var pendingAgentIdStr) &&
            bridgeSession.Vars.TryGetValue("_pending_interaction_id", out var pendingInteractionIdStr))
        {
            bridgeSession.Vars.Remove("_pending_agent_id");
            bridgeSession.Vars.Remove("_pending_interaction_id");
            bridgeSession.Vars.Remove("_agent_uuid");
            await _sessionStore.SaveAsync(bridgeSession, ct);

            if (Guid.TryParse(pendingAgentIdStr, out var pendingAgentId) &&
                Guid.TryParse(pendingInteractionIdStr, out var pendingInteractionId))
            {
                using var scope      = _scopeFactory.CreateScope();

                // Set TenantContext so FlowEngine (which uses ScopedTenantDbContextFactory)
                // can resolve the tenant schema — without this, StartAsync throws because
                // there is no HTTP request to populate TenantContext from middleware.
                var platformDb = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
                var tenant     = await platformDb.Tenants.FirstOrDefaultAsync(t => t.Id == bridgeSession.TenantId, ct);
                if (tenant is not null)
                    scope.ServiceProvider.GetRequiredService<TenantContext>().Current = tenant;

                var crmFlowEngine    = scope.ServiceProvider.GetRequiredService<IFlowEngine>();
                var telephonyEngine  = scope.ServiceProvider.GetRequiredService<ITelephonyFlowEngine>();

                var fireResult = await telephonyEngine.FireEventAsync(
                    uuid, "agent_answer",
                    new FireEventContext
                    {
                        AgentId       = pendingAgentId,
                        InteractionId = pendingInteractionId,
                        FlowEngine    = crmFlowEngine,
                    }, ct);

                if (fireResult.CrmFlowSession is not null)
                {
                    var sessionJson = JsonSerializer.Serialize(
                        fireResult.CrmFlowSession,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    await _hub.Clients
                        .Group($"agent:{pendingAgentId}")
                        .ReceiveScriptPop(sessionJson);
                }
            }
        }
        else
        {
            await _sessionStore.SaveAsync(bridgeSession, ct);
        }
    }

    /// <summary>
    /// CHANNEL_UNBRIDGE fires when a bridge between two channels is torn down.
    /// If the caller's channel still has a live session, hang it up so the call ends cleanly
    /// rather than leaving the caller's FreeSWITCH channel alive (which would cause QueuePollingService
    /// to re-deliver the call indefinitely).
    /// </summary>
    private async Task HandleChannelUnbridgeAsync(
        Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var uuid  = vars.GetValueOrDefault("Unique-ID") ?? "";
        var cause = vars.GetValueOrDefault("Hangup-Cause") ?? "";
        _logger.LogInformation("CHANNEL_UNBRIDGE {Uuid} cause={Cause}", uuid, cause);

        // Resolve via the session's own key OR its bridge partner's — this event can fire with
        // either leg's uuid, and the session is only ever keyed under the caller/parked leg's.
        var session = await ResolveSessionAsync(uuid, vars, ct);
        if (session is not null)
        {
            _logger.LogInformation(
                "CHANNEL_UNBRIDGE {Uuid}: caller channel still active (sessionKeyUuid={SessionKeyUuid}) — hanging up",
                uuid, session.ChannelUuid);
            await esl.HangupChannelAsync(session.ChannelUuid, ct);
        }
    }

    /// <summary>
    /// PLAYBACK_STOP fires when FreeSWITCH finishes playing a file/stream on a channel.
    /// If the Play node set up a loop, re-broadcast (handling periodic announcements).
    /// If not looping but a continuation node is configured, resume flow execution from that node.
    /// Also handles PLAYBACK_STOP on the agent's channel for whisper pre-bridge announcements.
    /// </summary>
    private async Task HandlePlaybackStopAsync(
        Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var uuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(uuid)) return;

        // Check if this PLAYBACK_STOP is from the agent's whisper channel (reverse mapping)
        var whisperCallerUuid = await _sessionStore.GetKeyAsync($"whisper:{uuid}", ct);
        if (whisperCallerUuid is not null)
        {
            await HandleWhisperPlaybackStopAsync(uuid, whisperCallerUuid, esl, ct);
            return;
        }

        var session = await _sessionStore.GetAsync(uuid, ct);
        if (session is null || !session.Vars.ContainsKey("_play_media_arg")) return;

        var isLoop     = session.Vars.GetValueOrDefault("_play_loop") == "true";
        var mediaArg   = session.Vars["_play_media_arg"];
        var audioSource = session.Vars.GetValueOrDefault("_play_audio_source", "file");
        var currentState = session.Vars.GetValueOrDefault("_play_state", "main");

        // ── Duration check ───────────────────────────────────────────────────────
        if (int.TryParse(session.Vars.GetValueOrDefault("_play_duration_seconds", "0"), out var durationSecs)
            && durationSecs > 0
            && DateTimeOffset.TryParse(session.Vars.GetValueOrDefault("_play_started_at", ""), out var startedAt)
            && (DateTimeOffset.UtcNow - startedAt).TotalSeconds >= durationSecs)
        {
            _logger.LogInformation("PlaybackStop [{Uuid}]: duration reached ({Secs}s)", uuid, durationSecs);
            var durationNextNode = session.Vars.GetValueOrDefault("_play_next_duration_reached");
            ClearPlayVars(session);
            await _sessionStore.SaveAsync(session, ct);

            if (!string.IsNullOrEmpty(durationNextNode))
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider
                    .GetRequiredService<ITelephonyFlowEngine>()
                    .ResumeFromNodeAsync(uuid, durationNextNode, esl, ct);
            }
            return;
        }

        // ── Announcement just finished — advance index, restart main ────────────
        if (currentState == "announcement")
        {
            // Advance to the next announcement in the playlist (wraps to 0 after the last)
            var announcements = GetAnnouncementList(session);
            if (announcements.Count > 0)
            {
                var currentIdx = int.TryParse(
                    session.Vars.GetValueOrDefault("_play_announcement_index", "0"), out var ci) ? ci : 0;
                var nextIdx = (currentIdx + 1) % announcements.Count;
                session.Vars["_play_announcement_index"] = nextIdx.ToString();
            }

            session.Vars["_play_state"]                = "main";
            session.Vars["_play_last_announcement_at"] = DateTimeOffset.UtcNow.ToString("O");

            if (isLoop)
            {
                await esl.BroadcastAsync(uuid, mediaArg, ct);
                await _sessionStore.SaveAsync(session, ct);
            }
            else
            {
                await FireEndTransitionAsync(session, uuid, audioSource, esl, ct);
            }
            return;
        }

        // ── Main file finished — check if a periodic announcement is due ─────────
        var announcementList = GetAnnouncementList(session);
        if (announcementList.Count > 0
            && int.TryParse(session.Vars.GetValueOrDefault("_play_announcement_interval", "30"), out var interval))
        {
            var lastAtStr = session.Vars.GetValueOrDefault("_play_last_announcement_at", "");
            var elapsed = DateTimeOffset.TryParse(lastAtStr, out var lastAt)
                ? (DateTimeOffset.UtcNow - lastAt).TotalSeconds
                : double.MaxValue;

            if (elapsed >= interval)
            {
                var idx = int.TryParse(
                    session.Vars.GetValueOrDefault("_play_announcement_index", "0"), out var ai) ? ai : 0;
                var announcementArg = announcementList[idx % announcementList.Count];

                _logger.LogInformation(
                    "PlaybackStop [{Uuid}]: playing announcement #{Idx}/{Total}", uuid, idx, announcementList.Count);
                session.Vars["_play_state"] = "announcement";
                await esl.BroadcastAsync(uuid, announcementArg, ct);
                await _sessionStore.SaveAsync(session, ct);
                return;
            }
        }

        if (isLoop)
        {
            await esl.BroadcastAsync(uuid, mediaArg, ct);
            await _sessionStore.SaveAsync(session, ct);
        }
        else
        {
            await FireEndTransitionAsync(session, uuid, audioSource, esl, ct);
        }
    }

    private async Task FireEndTransitionAsync(
        TelephonyCallSession session,
        string uuid,
        string audioSource,
        EslClient esl,
        CancellationToken ct)
    {
        var transitionKey = audioSource == "tts" ? "tts_finished" : "end_of_stream";
        var nextNode = session.Vars.GetValueOrDefault($"_play_next_{transitionKey}");
        ClearPlayVars(session);
        await _sessionStore.SaveAsync(session, ct);

        if (!string.IsNullOrEmpty(nextNode))
        {
            _logger.LogInformation("PlaybackStop [{Uuid}]: transition={Key} → node {NodeId}", uuid, transitionKey, nextNode);
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<ITelephonyFlowEngine>()
                .ResumeFromNodeAsync(uuid, nextNode, esl, ct);
        }
    }

    /// <summary>
    /// Dispatches FreeSWITCH CUSTOM events by Event-Subclass. Currently only mod_audio_stream's
    /// family is subscribed (see the "event plain ... CUSTOM ..." line in RunLoopAsync).
    /// </summary>
    private async Task HandleCustomEventAsync(
        Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        switch (vars.GetValueOrDefault("Event-Subclass"))
        {
            case "mod_audio_stream::disconnect":
                await HandleAudioStreamFinishedAsync(vars, esl, "disconnected", ct);
                break;
            case "mod_audio_stream::error":
                await HandleAudioStreamFinishedAsync(vars, esl, "error", ct);
                break;
            case "mod_audio_stream::connect":
                _logger.LogDebug("AudioStreamConnect [{Uuid}]", vars.GetValueOrDefault("Unique-ID"));
                break;
        }
    }

    /// <summary>
    /// mod_audio_stream::disconnect/::error — the streaming-TTS equivalent of PLAYBACK_STOP.
    /// The relay always calls "uuid_audio_stream stop" once synthesis ends (success or failure —
    /// see TtsStreamRelayEndpoints), so disconnect fires on every outcome; error is a secondary,
    /// diagnostic-only signal for the same underlying event, guarded against a double resume by
    /// the same "_play_state" check ClearPlayVars leaves behind after the first one lands.
    /// Resumes the same way as the flite path (FireEndTransitionAsync) — PlayNodeHandler's
    /// streaming branch stores the same "_play_next_{transition}" session vars either way.
    /// </summary>
    private async Task HandleAudioStreamFinishedAsync(
        Dictionary<string, string> vars, EslClient esl, string reason, CancellationToken ct)
    {
        var uuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(uuid)) return;

        var session = await _sessionStore.GetAsync(uuid, ct);
        if (session is null || session.Vars.GetValueOrDefault("_play_state") != "streaming") return;

        _logger.LogInformation("AudioStreamFinished [{Uuid}]: reason={Reason}", uuid, reason);
        await FireEndTransitionAsync(session, uuid, "tts", esl, ct);
    }

    /// <summary>
    /// PLAYBACK_STOP fired on the agent's parked channel (whisper announcement finished).
    /// Resumes the agent_selected event branch from the node after tf_whisper, which will
    /// eventually hit tf_end and call BridgeChannelsAsync to connect caller and agent.
    /// </summary>
    private async Task HandleWhisperPlaybackStopAsync(
        string agentUuid, string callerUuid, EslClient esl, CancellationToken ct)
    {
        _logger.LogInformation("WhisperPlaybackStop: agent={AgentUuid} caller={CallerUuid}", agentUuid, callerUuid);

        // Remove the reverse mapping — whisper is done
        await _sessionStore.DeleteKeyAsync($"whisper:{agentUuid}", ct);

        var session = await _sessionStore.GetAsync(callerUuid, ct);
        if (session is null) return;

        var nextNodeId = session.Vars.GetValueOrDefault("_whisper_next_default");

        // Clear whisper state vars
        var keysToRemove = session.Vars.Keys.Where(k => k.StartsWith("_whisper_")).ToList();
        foreach (var k in keysToRemove) session.Vars.Remove(k);
        await _sessionStore.SaveAsync(session, ct);

        if (!string.IsNullOrEmpty(nextNodeId))
        {
            // Continue event branch from the node after tf_whisper (typically tf_end → bridge)
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<ITelephonyFlowEngine>()
                .ResumeFromNodeAsync(callerUuid, nextNodeId, esl, ct);
        }
        else
        {
            // No continuation — whisper was the last node. Bridge directly.
            var freshSession = await _sessionStore.GetAsync(callerUuid, ct);
            if (freshSession?.Vars.TryGetValue("_agent_uuid", out var storedAgentUuid) == true
                && !string.IsNullOrEmpty(storedAgentUuid))
            {
                await esl.BridgeChannelsAsync(callerUuid, storedAgentUuid, ct);
                freshSession.Vars.Remove("_agent_uuid");
                await _sessionStore.SaveAsync(freshSession, ct);
            }
        }
    }

    private static List<string> GetAnnouncementList(TelephonyCallSession session)
    {
        var json = session.Vars.GetValueOrDefault("_play_announcements_json", "");
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private static void ClearPlayVars(TelephonyCallSession session)
    {
        var keysToRemove = session.Vars.Keys
            .Where(k => k.StartsWith("_play_"))
            .ToList();
        foreach (var k in keysToRemove)
            session.Vars.Remove(k);
    }

    private async Task HandleHangupByTenantScanAsync(string channelUuid, string cause, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory   = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var platformDb  = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();

        var tenants = await platformDb.Tenants.Where(t => t.IsActive).ToListAsync(ct);
        foreach (var tenant in tenants)
        {
            await using var db = dbFactory.Create(tenant.SchemaName);
            var record = await db.CallRecords.FirstOrDefaultAsync(
                r => r.ContactIdExternal == channelUuid, ct);
            if (record is null) continue;

            record.Complete();
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "CHANNEL_HANGUP {Uuid} cause={Cause} → CallRecord {RecordId} completed (tenant scan, tenant={Tenant}) — " +
                "no call_disconnected event fired, no agent-state restore, no session cleanup for this uuid",
                channelUuid, cause, record.Id, tenant.Subdomain);
            return;
        }
    }
}
