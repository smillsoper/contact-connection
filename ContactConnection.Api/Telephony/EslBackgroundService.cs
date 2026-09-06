using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
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
    private readonly TtsPlaybackCoordinator _ttsPlaybackCoordinator;

    public EslBackgroundService(
        IHubContext<FlowHub, IFlowHubClient> hub,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<EslBackgroundService> logger,
        ILogger<EslClient> eslClientLogger,
        ITelephonyCallSessionStore sessionStore,
        IAgentStateStore stateStore,
        TtsPlaybackCoordinator ttsPlaybackCoordinator)
    {
        _hub                    = hub;
        _scopeFactory           = scopeFactory;
        _config                 = config;
        _logger                 = logger;
        _eslClientLogger        = eslClientLogger;
        _sessionStore           = sessionStore;
        _stateStore             = stateStore;
        _ttsPlaybackCoordinator = ttsPlaybackCoordinator;
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
        // ::connect is diagnostic only; ::play carries one decoded audio chunk's temp-file path —
        // mod_audio_stream never plays audio itself, it's on us to uuid_broadcast each one (see
        // HandleAudioStreamPlayAsync); ::disconnect/::error signal no more chunks are coming, but
        // the flow only actually resumes once the last queued chunk has finished playing, not the
        // instant the vendor stream ends (see HandleAudioStreamFinishedAsync).
        await esl.SubscribeAsync(
            "CHANNEL_PARK CHANNEL_ANSWER CHANNEL_HANGUP CHANNEL_HANGUP_COMPLETE CHANNEL_BRIDGE CHANNEL_UNBRIDGE " +
            "CHANNEL_HOLD CHANNEL_UNHOLD PLAYBACK_STOP " +
            "CUSTOM mod_audio_stream::connect mod_audio_stream::play mod_audio_stream::disconnect mod_audio_stream::error " +
            "contactconnection::ivr_done contactconnection::vm_done contactconnection::xfer_failed", ct);

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
                case "CHANNEL_ANSWER":
                    HandleChannelAnswerLog(vars);
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
                case "CHANNEL_HOLD":
                    await HandleChannelHoldAsync(vars, esl, mask: true, ct);
                    break;
                case "CHANNEL_UNHOLD":
                    await HandleChannelHoldAsync(vars, esl, mask: false, ct);
                    break;
                case "CUSTOM":
                    await HandleCustomEventAsync(vars, msg.EventBody, esl, ct);
                    break;
            }
        }
    }

    private async Task HandleChannelParkAsync(
        Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        // Prefer cc_did — the public dialplan normalizes the called number to full
        // E.164 there. Fall back to the raw channel field for non-dialplan paths
        // (e.g. direct agent-extension parks).
        var destination  = vars.GetValueOrDefault("variable_cc_did")
                        ?? vars.GetValueOrDefault("Caller-Destination-Number") ?? "";
        var channelUuid  = vars.GetValueOrDefault("Unique-ID") ?? "";

        if (string.IsNullOrEmpty(destination) || string.IsNullOrEmpty(channelUuid)) return;

        // Whisper channels created by AnswerQueuedCall carry cc_whisper=true.
        // Skip them — they are internal bridge legs, not new inbound calls.
        if (vars.GetValueOrDefault("variable_cc_whisper") == "true") return;

        // A tf_ivr_menu node uuid_transfers the caller into the ivr_collect extension, which
        // re-parks when play_and_get_digits finishes. That re-park is not a new inbound call —
        // the contactconnection::ivr_done event drives the flow resume. Identify it by FreeSWITCH's
        // own transfer bookkeeping (destination + transfer source), not just the session var —
        // HandleIvrDoneAsync may have already cleared _ivr_in_progress by the time this fires.
        var transferSource = vars.GetValueOrDefault("variable_transfer_source")
                          ?? vars.GetValueOrDefault("Caller-Transfer-Source") ?? "";
        var rawDestination = vars.GetValueOrDefault("Caller-Destination-Number") ?? "";
        var ivrSession = await _sessionStore.GetAsync(channelUuid, ct);
        if (destination == "ivr_collect"
            || rawDestination == "ivr_collect"
            || transferSource.Contains("ivr_collect")
            || ivrSession?.Vars.GetValueOrDefault("_ivr_in_progress") == "true")
        {
            _logger.LogInformation("CHANNEL_PARK {Uuid}: returned from IVR collection — not a new call", channelUuid);
            return;
        }

        // Same idea for tf_voicemail: the vm_record extension re-parks after recording;
        // contactconnection::vm_done drives the resume, not a fresh inbound.
        if (destination == "vm_record"
            || rawDestination == "vm_record"
            || transferSource.Contains("vm_record")
            || ivrSession?.Vars.GetValueOrDefault("_vm_in_progress") == "true")
        {
            _logger.LogInformation("CHANNEL_PARK {Uuid}: returned from voicemail recording — not a new call", channelUuid);
            return;
        }

        // Same idea for tf_transfer's external_number destination: the xfer_bridge extension only
        // re-parks the caller when the bridge FAILED to connect (a successful bridge goes straight
        // to CHANNEL_BRIDGE and never comes back here). So a re-park with _xfer_in_progress still
        // set IS the failure signal — resume on the node's `failed` handle. HandleXferFailedAsync
        // is idempotent with the contactconnection::xfer_failed event path (first to clear wins).
        if (destination == "xfer_bridge"
            || rawDestination == "xfer_bridge"
            || transferSource.Contains("xfer_bridge")
            || ivrSession?.Vars.GetValueOrDefault("_xfer_in_progress") == "true")
        {
            _logger.LogInformation("CHANNEL_PARK {Uuid}: returned from failed external transfer — resuming failed branch", channelUuid);
            await HandleXferFailedAsync(vars, esl, ct);
            return;
        }

        // Skip outbound channels — when testing with "fs_cli originate ... &park()", FreeSWITCH
        // fires CHANNEL_PARK for both the originate A-leg (outbound) and the loopback B-leg
        // (inbound). Only the inbound B-leg is the real caller; the A-leg must not be
        // processed as a second duplicate call.
        // NOTE: the actual event header is "Call-Direction", not "Channel-Call-Direction" —
        // the previous check never matched anything, so this never actually skipped the A-leg,
        // producing a duplicate CallRecord (queued, screen-popped, and answerable independently)
        // for every self-dial test call.
        // EXCEPTION: a fired callback (CallbackProcessingService originates "...&park()" with
        // cc_callback_id + cc_did) — that answered outbound leg IS the real party and must run
        // the campaign's inbound flow so it queues to an agent, exactly like a fresh DID call.
        // Same for a queue-callback dial leg (cc_qcb_reserved_agent_id) — it bridges to the
        // reserved agent instead (handled in HandleDidCallAsync).
        if (vars.GetValueOrDefault("Call-Direction") == "outbound"
            && string.IsNullOrEmpty(vars.GetValueOrDefault("variable_cc_callback_id"))
            && string.IsNullOrEmpty(vars.GetValueOrDefault("variable_cc_qcb_reserved_agent_id"))) return;

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
        var callbackConn     = scope.ServiceProvider.GetRequiredService<IScheduledCallbackConnectionService>();

        // Queue-callback dial leg answered — bridge it to the reserved agent, no inbound flow.
        if (!string.IsNullOrEmpty(vars.GetValueOrDefault("variable_cc_qcb_reserved_agent_id")))
        {
            var qcbDelivery = scope.ServiceProvider.GetRequiredService<QueueCallbackDeliveryService>();
            if (await qcbDelivery.ConnectAnsweredLegAsync(channelUuid, vars, esl, ct)) return;
        }

        // ── DID routing: check if destination matches a provisioned phone number ──
        // Carriers vary on whether the dialed number carries a leading "+" and/or "1",
        // and stored DIDs may be in any of those forms — match on a set of equivalent
        // representations rather than an exact string.
        var digits = new string(destination.Where(char.IsDigit).ToArray());
        var last10 = digits.Length >= 10 ? digits[^10..] : digits;
        var didForms = new[]
        {
            destination, digits, "+" + digits,
            last10, "1" + last10, "+1" + last10,
        };
        var routing = await platformDb.PhoneNumberRoutings
            .FirstOrDefaultAsync(r => r.IsActive && didForms.Contains(r.Number), ct);

        if (routing is not null)
        {
            await HandleDidCallAsync(
                routing, callerNumber, callerName, channelUuid, destination, vars,
                esl, telephonyEngine, platformDb, dbFactory, callStateRecorder, callbackConn, ct);
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
        IScheduledCallbackConnectionService callbackConn,
        CancellationToken ct)
    {
        var tenant = await platformDb.Tenants.FirstOrDefaultAsync(t => t.Id == routing.TenantId, ct);
        if (tenant is null)
        {
            _logger.LogWarning("CHANNEL_PARK DID {Uuid}: tenant {TenantId} not found", channelUuid, routing.TenantId);
            return;
        }

        await using var db = dbFactory.Create(tenant.SchemaName);

        // A fired scheduled-callback leg carries cc_callback_id — its connected call record is a
        // "callback" source record, and ScheduledCallbackConnectionService links it back + marks
        // the row completed once the flow has run (below). cc_target_flow_id (when set) says which
        // telephony flow to run for it instead of the campaign default — this is what keeps a
        // scheduled callback from re-entering the inbound flow and re-offering itself.
        var callbackIdRaw = eventVars.GetValueOrDefault("variable_cc_callback_id");
        var isCallbackLeg = Guid.TryParse(callbackIdRaw, out var callbackId);
        Guid? targetFlowId =
            Guid.TryParse(eventVars.GetValueOrDefault("variable_cc_target_flow_id"), out var tf) ? tf : null;

        var record = isCallbackLeg
            ? CallRecord.CreateCallback(tenant.Id, routing.CampaignId, callerNumber)
            : CallRecord.CreateInbound(
                tenantId: tenant.Id, callerId: callerNumber, agentId: null, contactIdExternal: channelUuid);

        record.SetContactIdExternal(channelUuid);
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
            FlowIdOverride    = targetFlowId,
        };

        await telephonyEngine.ExecuteAsync(ctx, ct);

        // Fired callback connected — mark the Callback row completed and link this call record.
        if (isCallbackLeg)
            await callbackConn.MarkConnectedAsync(tenant.SchemaName, tenant.Id, callbackId, record.Id, ct);

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
    /// Diagnostic-only: CHANNEL_ANSWER tells us exactly when a leg answered. For the
    /// agent/caller bridge bug we care most about the whisper agent leg (cc_whisper=true) —
    /// this pins down whether the originate produced a real answer and how it lines up in
    /// time with the whisper broadcast and the eventual CHANNEL_BRIDGE.
    /// </summary>
    private void HandleChannelAnswerLog(Dictionary<string, string> vars)
    {
        var uuid      = vars.GetValueOrDefault("Unique-ID") ?? "";
        var isWhisper = vars.GetValueOrDefault("variable_cc_whisper") == "true";
        var name      = vars.GetValueOrDefault("Channel-Name") ?? "";
        var callState = vars.GetValueOrDefault("Channel-Call-State") ?? "";
        _logger.LogInformation(
            "CHANNEL_ANSWER {Uuid} whisper={IsWhisper} name={Name} callState={CallState}",
            uuid, isWhisper, name, callState);
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
    /// The ivr_collect dialplan extension finished play_and_get_digits for a tf_ivr_menu node and
    /// emitted this custom event with the result. Resolve the digits to a transition and resume.
    /// </summary>
    private async Task HandleIvrDoneAsync(
        Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var uuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(uuid)) return;

        var session = await ResolveSessionAsync(uuid, vars, ct);
        if (session is null || session.Vars.GetValueOrDefault("_ivr_in_progress") != "true") return;

        // The collected digits ride on the event: our custom cc_ivr_digits header, or the
        // channel var it's set from (variable_cc_ivr_result, auto-included on channel events).
        // Empty = no valid entry (timed out / retries exhausted) → falls through to no_match.
        // Do NOT fall back to uuid_getvar here — reading an api reply from inside an event
        // handler races the queued CHANNEL_PARK the extension's trailing park() emits.
        var digits = vars.GetValueOrDefault("cc_ivr_digits");
        if (string.IsNullOrEmpty(digits))
            digits = vars.GetValueOrDefault("variable_cc_ivr_result");

        Dictionary<string, string> optionMap;
        try
        {
            optionMap = JsonSerializer.Deserialize<Dictionary<string, string>>(
                session.Vars.GetValueOrDefault("_ivr_options", "{}")) ?? new();
        }
        catch (JsonException)
        {
            optionMap = new();
        }

        var noMatch = session.Vars.GetValueOrDefault("_ivr_no_match");
        var target = IvrMenu.ResolveTarget(digits, optionMap, string.IsNullOrEmpty(noMatch) ? null : noMatch);

        session.Vars.Remove("_ivr_in_progress");
        session.Vars.Remove("_ivr_options");
        session.Vars.Remove("_ivr_no_match");
        await _sessionStore.SaveAsync(session, ct);

        _logger.LogInformation(
            "ivr_done {Uuid}: digits='{Digits}' → node {Target}",
            uuid, digits ?? "(none)", target ?? "(dead-end)");

        if (string.IsNullOrEmpty(target)) return;

        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<ITelephonyFlowEngine>()
            .ResumeFromNodeAsync(session.ChannelUuid, target, esl, ct);
    }

    /// <summary>
    /// The vm_record dialplan extension finished recording a caller message for a tf_voicemail
    /// node and emitted this custom event. Below the minimum length → resume on <c>no_message</c>.
    /// Otherwise: move the .wav into blob storage, write the <see cref="Voicemail"/> row, run the
    /// node's optional email delivery (variable-resolved subject/body, .wav attached), push a
    /// supervisor SignalR notification, and resume on <c>recorded</c>.
    /// </summary>
    private async Task HandleVmDoneAsync(
        Dictionary<string, string> vars, EslClient esl, CancellationToken ct,
        bool fromHangup = false, TelephonyCallSession? knownSession = null)
    {
        var uuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(uuid)) return;

        // The hangup-salvage caller hands us the session it already resolved and claimed (see
        // HandleChannelHangupCoreAsync) — re-resolving here would race the session teardown that
        // runs right after. The contactconnection::vm_done event path resolves + guards normally.
        var session = knownSession ?? await ResolveSessionAsync(uuid, vars, ct);
        if (session is null) return;
        if (knownSession is null && session.Vars.GetValueOrDefault("_vm_in_progress") != "true") return;

        // Recorded length: our custom header first, then FreeSWITCH's own record_ms / record_seconds.
        var recordedMs = ParseFirstInt(
            vars.GetValueOrDefault("cc_vm_recorded_ms"),
            vars.GetValueOrDefault("variable_record_ms"));
        if (recordedMs == 0 &&
            int.TryParse(vars.GetValueOrDefault("variable_record_seconds"), out var recSecs))
            recordedMs = recSecs * 1000;

        var containerPath = session.Vars.GetValueOrDefault("_vm_path")
                            ?? vars.GetValueOrDefault("cc_vm_recorded_path")
                            ?? string.Empty;
        var nodeId          = session.Vars.GetValueOrDefault("_vm_node_id") ?? string.Empty;
        var nextRecorded    = session.Vars.GetValueOrDefault("_vm_next_recorded");
        var nextNoMessage   = session.Vars.GetValueOrDefault("_vm_next_no_message");
        var minMs           = int.TryParse(session.Vars.GetValueOrDefault("_vm_min_ms"), out var m) ? m : 0;

        foreach (var k in new[] { "_vm_in_progress", "_vm_node_id", "_vm_next_recorded", "_vm_next_no_message", "_vm_min_ms", "_vm_path" })
            session.Vars.Remove(k);
        // Salvage path: the session is about to be deleted by the hangup handler and was already
        // claimed there (_vm_in_progress = "salvaging"), so persisting the cleared copy would only
        // race that delete and risk resurrecting the key. The event path still needs the save.
        if (!fromHangup)
            await _sessionStore.SaveAsync(session, ct);

        var hostPath = HostRecordingPath(containerPath);
        var fileLen  = !string.IsNullOrEmpty(hostPath) && File.Exists(hostPath) ? new FileInfo(hostPath).Length : 0;

        // Caller hung up mid-message: no record_ms header arrives. Estimate from the .wav —
        // read the actual byte-rate out of its header (FreeSWITCH records at the channel rate,
        // which on carrier/WebRTC legs is 48 kHz, not the 8 kHz a hard-coded constant assumed).
        if (recordedMs == 0 && fileLen > 44)
            recordedMs = EstimateWavDurationMs(hostPath!, fileLen);

        // ── Too short / nothing recorded → no_message ───────────────────────────
        if (recordedMs < minMs || fileLen == 0)
        {
            _logger.LogInformation(
                "vm_done {Uuid}: no usable message (ms={Ms}, min={Min}, file={File}, bytes={Bytes}) → no_message",
                uuid, recordedMs, minMs, hostPath ?? "(none)", fileLen);
            TryDeleteFile(hostPath);
            if (!fromHangup) await ResumeAsync(session.ChannelUuid, nextNoMessage, esl, ct);
            return;
        }

        var durationSeconds = (int)Math.Round(recordedMs / 1000.0);

        using var scope = _scopeFactory.CreateScope();
        var sp          = scope.ServiceProvider;
        var dbFactory   = sp.GetRequiredService<ITenantDbContextFactory>();
        var blobs       = sp.GetRequiredService<IBlobStorage>();

        var voicemail = Voicemail.Create(
            session.TenantId, session.CallRecordId, session.CampaignId, session.CallerNumber, durationSeconds);

        byte[] audio;
        try
        {
            audio = await File.ReadAllBytesAsync(hostPath!, ct);
            await using var ms = new MemoryStream(audio, writable: false);
            await blobs.PutAsync(voicemail.StorageKey, ms, "audio/wav", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "vm_done {Uuid}: failed to move recording into blob storage from {Path}", uuid, hostPath);
            if (!fromHangup) await ResumeAsync(session.ChannelUuid, nextNoMessage, esl, ct);
            return;
        }

        await using (var db = dbFactory.Create(session.TenantSchemaName))
        {
            db.Voicemails.Add(voicemail);
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "vm_done {Uuid}: voicemail {VmId} stored ({Secs}s) for call {CallId}",
            uuid, voicemail.Id, durationSeconds, session.CallRecordId);

        // ── Optional email delivery ────────────────────────────────────────────
        await DeliverVoicemailEmailAsync(sp, dbFactory, session, nodeId, voicemail, audio, ct);

        // ── Supervisor SignalR push ───────────────────────────────────────────
        try
        {
            await sp.GetRequiredService<IDashboardNotifier>().NotifyVoicemailReceivedAsync(
                session.TenantId, session.CampaignId, voicemail.Id, session.CallRecordId,
                session.CallerNumber, durationSeconds, voicemail.CreatedAt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "vm_done {Uuid}: supervisor notification failed", uuid);
        }

        TryDeleteFile(hostPath);
        if (!fromHangup) await ResumeAsync(session.ChannelUuid, nextRecorded, esl, ct);
    }

    /// <summary>
    /// Resolves the tf_voicemail node's delivery block from the cached flow definition, renders
    /// its templated fields against a variable context built from the call, and sends via
    /// <see cref="IEmailService"/> with the message .wav attached. The outcome
    /// (sent / failed / skipped) is stamped on the voicemail row.
    /// </summary>
    private async Task DeliverVoicemailEmailAsync(
        IServiceProvider sp, ITenantDbContextFactory dbFactory, TelephonyCallSession session,
        string nodeId, Voicemail voicemail, byte[] audio, CancellationToken ct)
    {
        string status;
        string? recipients = null;
        string? error = null;
        try
        {
            JsonObject? node = null;
            if (!string.IsNullOrEmpty(nodeId) && !string.IsNullOrEmpty(session.FlowDefinitionJson))
                node = JsonNode.Parse(session.FlowDefinitionJson)?["nodes"]?[nodeId]?.AsObject();

            if (node is null)
            {
                status = VoicemailEmailStatus.Skipped;
            }
            else
            {
                var resolver = sp.GetRequiredService<IVariableResolver>();
                var varCtx   = await BuildVoicemailVarContextAsync(dbFactory, session, ct);
                var attachment = new EmailAttachment($"voicemail-{session.CallRecordId}.wav", audio, "audio/wav");

                var message = VoicemailEmail.Build(node, resolver, varCtx, attachment);
                if (message is null || !message.HasRecipients)
                {
                    status = VoicemailEmailStatus.Skipped;
                }
                else
                {
                    recipients = string.Join(", ", message.To.Concat(message.Cc).Concat(message.Bcc));
                    await sp.GetRequiredService<IEmailService>().SendAsync(message, ct);
                    status = VoicemailEmailStatus.Sent;
                }
            }
        }
        catch (Exception ex)
        {
            status = VoicemailEmailStatus.Failed;
            error  = ex.Message;
            _logger.LogError(ex, "vm_done: voicemail {VmId} email delivery failed", voicemail.Id);
        }

        try
        {
            await using var db = dbFactory.Create(session.TenantSchemaName);
            var row = await db.Voicemails.FirstOrDefaultAsync(v => v.Id == voicemail.Id, ct);
            row?.RecordEmailDelivery(status, recipients, error);
            if (row is not null) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "vm_done: failed to persist email-delivery outcome for voicemail {VmId}", voicemail.Id);
        }
    }

    private async Task<VariableContext> BuildVoicemailVarContextAsync(
        ITenantDbContextFactory dbFactory, TelephonyCallSession session, CancellationToken ct)
    {
        var ctx = new VariableContext
        {
            Tenant = { ["id"] = session.TenantId.ToString(), ["subdomain"] = session.TenantSubdomain },
        };
        ctx.Caller["phone"] = session.CallerNumber;
        ctx.Caller["ani"]   = session.CallerNumber;
        ctx.Caller["dnis"]  = session.DestinationNumber;
        ctx.CallRecord["id"] = session.CallRecordId.ToString();
        ctx.CallRecord["dnis"] = session.DestinationNumber;
        ctx.CallRecord["campaign_id"] = session.CampaignId.ToString();

        foreach (var (k, v) in session.Vars)
            if (!k.StartsWith('_')) ctx.FlowVars[k] = v;

        try
        {
            await using var db = dbFactory.Create(session.TenantSchemaName);
            var r = await db.CallRecords.FirstOrDefaultAsync(x => x.Id == session.CallRecordId, ct);
            if (r is not null)
            {
                ctx.CallRecord["status"]        = r.OverallStatus;
                ctx.CallRecord["account_number"] = r.AccountNumber ?? "";
                ctx.CallRecord["call_started_at"] = r.CallStartAt?.ToString("O") ?? "";
                ctx.Caller["first_name"] = r.FirstName ?? "";
                ctx.Caller["last_name"]  = r.LastName ?? "";
                ctx.Caller["email"]      = r.Email ?? "";
                ctx.Caller["name"] = string.Join(" ",
                    new[] { r.FirstName, r.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "BuildVoicemailVarContext: call record load failed (non-fatal)");
        }

        return ctx;
    }

    private string? HostRecordingPath(string containerPath)
    {
        if (string.IsNullOrEmpty(containerPath)) return null;
        var fileName = Path.GetFileName(containerPath);
        // Default mirrors TtsFileSynthesizer.ResolveCacheHostDir / AudioFilesEndpoints.
        // ResolveSoundsHostPath: the process's working directory is this project's own folder
        // (ContactConnection.Api), one level below the repo root that docker-compose.yml's
        // "./freeswitch/recordings" volume mount is relative to — the missing ".." here meant
        // every voicemail recording resolved to a nonexistent ContactConnection.Api\freeswitch\
        // recordings\ path, so HandleVmDoneAsync always saw 0 bytes and took no_message no matter
        // how long the caller actually spoke (the real .wav was untouched, just never found).
        var hostDir = _config["FreeSWITCH:RecordingsHostPath"] ?? Path.Combine("..", "freeswitch", "recordings");
        return Path.GetFullPath(Path.Combine(hostDir, fileName));
    }

    private static int ParseFirstInt(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (int.TryParse(c, out var v)) return v;
        return 0;
    }

    /// <summary>
    /// Duration estimate for a voicemail .wav when FreeSWITCH gave us no record_ms (caller hung
    /// up mid-message). Reads the canonical 44-byte PCM WAV header's ByteRate field (bytes/sec of
    /// audio) rather than assuming a fixed sample rate — carrier and WebRTC legs record at 48 kHz,
    /// not 8 kHz. Falls back to the 16 kB/s (8 kHz/16-bit/mono) assumption if the header can't be read.
    /// </summary>
    private int EstimateWavDurationMs(string path, long fileLen)
    {
        const int headerBytes = 44;
        const int byteRateOffset = 28;
        try
        {
            Span<byte> header = stackalloc byte[headerBytes];
            using var fs = File.OpenRead(path);
            if (fs.Read(header) == headerBytes
                && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F')
            {
                var byteRate = BinaryPrimitives.ReadUInt32LittleEndian(header[byteRateOffset..]);
                if (byteRate > 0)
                    return (int)Math.Round((fileLen - headerBytes) / (double)byteRate * 1000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "EstimateWavDurationMs: header read failed for {Path}, using 8 kHz fallback", path);
        }
        return (int)Math.Round((fileLen - headerBytes) / 16000.0 * 1000);
    }

    private static void TryDeleteFile(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch { /* best effort — recordings mount */ }
    }

    private async Task ResumeAsync(string channelUuid, string? nodeId, EslClient esl, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<ITelephonyFlowEngine>()
            .ResumeFromNodeAsync(channelUuid, nodeId, esl, ct);
    }

    /// <summary>
    /// Agent placed the caller on/off hold (SIP re-INVITE → FreeSWITCH CHANNEL_HOLD/UNHOLD, fired
    /// on the agent leg). When the call's campaign has AutoMaskOnHold set and a recording is
    /// running, mask/unmask it for the hold span so hold-time audio isn't captured.
    /// </summary>
    private async Task HandleChannelHoldAsync(
        Dictionary<string, string> vars, EslClient esl, bool mask, CancellationToken ct)
    {
        var eventUuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(eventUuid)) return;

        var session = await ResolveSessionAsync(eventUuid, vars, ct);
        if (session is null || session.CampaignId == Guid.Empty) return;

        using var scope         = _scopeFactory.CreateScope();
        var dbFactory           = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var recordingController = scope.ServiceProvider.GetRequiredService<ICallRecordingController>();

        await using var db = dbFactory.Create(session.TenantSchemaName);

        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == session.CampaignId, ct);
        if (campaign?.AutoMaskOnHold != true) return;

        var record = await db.CallRecords.FirstOrDefaultAsync(r => r.ContactIdExternal == session.ChannelUuid, ct);
        if (record is null || record.RecordingStartedAt is null || record.RecordingStoppedAt is not null)
            return;   // no recording running to mask

        var command = new RecordingCommand
        {
            ChannelUuid      = session.ChannelUuid,
            CallRecordId     = record.Id,
            TenantSchemaName = session.TenantSchemaName,
            Source           = RecordingEventSource.AutoHold,
            Reason           = mask ? "agent_hold" : "agent_unhold",
        };

        var outcome = mask
            ? await recordingController.MaskAsync(new RecordingMaskCommand
              {
                  ChannelUuid = command.ChannelUuid, CallRecordId = command.CallRecordId,
                  TenantSchemaName = command.TenantSchemaName, Source = command.Source, Reason = command.Reason,
                  MaskFill = MaskFillKind.Silence,
              }, esl, ct)
            : await recordingController.UnmaskAsync(command, esl, ct);

        _logger.LogInformation(
            "CHANNEL_{Evt} {Uuid} → recording {Action} (AutoMaskOnHold) call={CallRecordId} ok={Ok}",
            mask ? "HOLD" : "UNHOLD", eventUuid, mask ? "masked" : "unmasked", record.Id, outcome.Ok);
    }

    /// <summary>
    /// Channel hung up — fire the call_disconnected event branch (if configured), mark the
    /// CallRecord complete, and delete the live call session from Redis.
    /// </summary>
    private async Task HandleChannelHangupAsync(Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await HandleChannelHangupCoreAsync(vars, esl, ct);
        }
        finally
        {
            if (sw.ElapsedMilliseconds > 1000)
                _logger.LogWarning(
                    "HandleChannelHangupAsync SLOW: {Uuid} event={Event} took {Ms}ms — ESL event loop was stalled this long",
                    vars.GetValueOrDefault("Unique-ID"), vars.GetValueOrDefault("Event-Name"), sw.ElapsedMilliseconds);
        }
    }

    private async Task HandleChannelHangupCoreAsync(Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var channelUuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(channelUuid)) return;

        var cause = vars.GetValueOrDefault("Hangup-Cause") ?? "unknown";

        // Check if there is a live session for this call (DID calls always have one) — the
        // session may be keyed under this event's own uuid or its bridge partner's (see
        // ResolveSessionAsync). session.ChannelUuid (not the raw event uuid) is the correct key
        // for all session-store/CallRecord lookups below once a session is found.
        var session = await ResolveSessionAsync(channelUuid, vars, ct);

        // A fired callback leg that hung up with no session — HandleChannelParkAsync never ran
        // for it, so the callee never answered (no answer / busy / gateway reject). Resolve the
        // Callback row: retry if attempts remain, else abandon. A callback that DID connect has a
        // session (keyed at park) on the first CHANNEL_HANGUP, so it falls through to the normal
        // completion path; we skip the trailing CHANNEL_HANGUP_COMPLETE (session already torn
        // down) so it can't be misread as a no-answer. MarkNoAnswerAsync is a no-op unless the
        // row is still 'attempted', which is the real backstop either way.
        var callbackIdRaw = vars.GetValueOrDefault("variable_cc_callback_id");
        if (session is null
            && vars.GetValueOrDefault("Event-Name") == "CHANNEL_HANGUP"
            && Guid.TryParse(callbackIdRaw, out var hungCallbackId))
        {
            var cbSchema = vars.GetValueOrDefault("variable_cc_tenant_schema");
            Guid.TryParse(vars.GetValueOrDefault("variable_cc_tenant_id"), out var cbTenantId);
            if (!string.IsNullOrEmpty(cbSchema))
            {
                using var cbScope = _scopeFactory.CreateScope();
                var callbackConn = cbScope.ServiceProvider.GetRequiredService<IScheduledCallbackConnectionService>();
                var abandoned = await callbackConn.MarkNoAnswerAsync(cbSchema, cbTenantId, hungCallbackId, cause, ct);
                _logger.LogInformation(
                    "CHANNEL_HANGUP {Uuid} cause={Cause}: fired callback {CallbackId} leg ended with no session " +
                    "(abandoned={Abandoned})",
                    channelUuid, cause, hungCallbackId, abandoned);
            }
            return;
        }

        // A queue-callback dial leg that hung up with no session — the caller never answered
        // (no answer / busy / gateway reject / originate timeout). Release the reserved agent and
        // either re-queue the placeholder (attempts remain) or record a callback abandon.
        if (session is null
            && vars.GetValueOrDefault("Event-Name") == "CHANNEL_HANGUP"
            && !string.IsNullOrEmpty(vars.GetValueOrDefault("variable_cc_qcb_reserved_agent_id")))
        {
            using var qcbScope = _scopeFactory.CreateScope();
            var qcbDelivery = qcbScope.ServiceProvider.GetRequiredService<QueueCallbackDeliveryService>();
            await qcbDelivery.HandleFailedLegAsync(vars, cause, ct);
            return;
        }

        // A queue-callback placeholder: the caller opted into virtual hold (tf_queue_callback) and
        // has now hung up. Keep the session alive as the queue placeholder — QueuePollingService
        // reserves an agent and dials back. Never complete the record / record an abandon / delete
        // the session here; the placeholder's own lifecycle is the outcome. Fires for both
        // CHANNEL_HANGUP and the trailing CHANNEL_HANGUP_COMPLETE — log once.
        if (session is not null
            && session.Vars.GetValueOrDefault("_queue_callback") == "true"
            && string.IsNullOrEmpty(session.Vars.GetValueOrDefault("_queue_callback_reserved_agent_id"))
            && string.IsNullOrEmpty(session.Vars.GetValueOrDefault("_assigned_agent_id")))
        {
            if (vars.GetValueOrDefault("Event-Name") == "CHANNEL_HANGUP")
            {
                using var qcbScope = _scopeFactory.CreateScope();
                var stateRecorder = qcbScope.ServiceProvider.GetRequiredService<ICallStateHistoryRecorder>();
                await stateRecorder.RecordAsync(
                    session.TenantId, session.TenantSchemaName, session.CallRecordId,
                    CallHistoryState.PostAgent, session.CampaignId, agentId: null,
                    detail: "Caller hung up — holding queue position for callback", ct: ct);
                _logger.LogInformation(
                    "CHANNEL_HANGUP {Uuid} cause={Cause}: queue-callback placeholder kept in queue (position held)",
                    channelUuid, cause);
            }
            return;
        }

        // tf_transfer external_number: the OUTBOUND leg (not the caller's own channel) died while
        // the transfer was still in progress → the bridge never connected. Leave the session and
        // call record alone — the caller's channel is still up and will re-park, at which point
        // HandleChannelParkAsync drives the node's `failed` branch. (A connected transfer clears
        // _xfer_in_progress on CHANNEL_BRIDGE, so this only fires on genuine connect failures.)
        if (session is not null
            && session.Vars.GetValueOrDefault("_xfer_in_progress") == "true"
            && !string.Equals(channelUuid, session.ChannelUuid, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "CHANNEL_HANGUP {Uuid} cause={Cause}: external-transfer outbound leg failed — caller {Caller} stays up for the failed branch",
                channelUuid, cause, session.ChannelUuid);
            return;
        }

        // Caller hung up while leaving a voicemail (never pressed # / hit silence). The vm_record
        // extension's trailing event+park never ran — salvage the .wav before the session is torn
        // down. Claim it synchronously (cheap Redis write) so a racing contactconnection::vm_done
        // event can't also store the message, then run the slow part (file read → blob → DB row →
        // email → SignalR, ~1.5s incl. the Resend call) off the ESL event loop instead of stalling
        // every telephony event behind it. HandleVmDoneAsync skips the flow resume when fromHangup.
        if (session?.Vars.GetValueOrDefault("_vm_in_progress") == "true")
        {
            session.Vars["_vm_in_progress"] = "salvaging";
            try { await _sessionStore.SaveAsync(session, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Voicemail salvage claim failed for {Uuid}", channelUuid); }

            var salvageSession = session;
            var salvageVars    = vars;
            var salvageUuid    = channelUuid;
            _ = Task.Run(async () =>
            {
                try { await HandleVmDoneAsync(salvageVars, esl, CancellationToken.None, fromHangup: true, knownSession: salvageSession); }
                catch (Exception ex) { _logger.LogError(ex, "Voicemail salvage on hangup failed for {Uuid}", salvageUuid); }
            });
        }

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
            var recordingController = scope.ServiceProvider.GetRequiredService<ICallRecordingController>();

            // Fire the call_disconnected event so the designer branch can run post-call actions
            // (which may itself include a tf_record(stop) node — handled before the safety net below)
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

                // Recording safety net: always drop watchdog timers for this channel; if a
                // recording was running and nothing closed it (no tf_record(stop) wired into
                // the disconnect branch), close the audit trail now. FreeSWITCH already stopped
                // the physical recording when the channel died.
                recordingController.ForgetChannel(sessionUuid);
                if (record.RecordingStartedAt is not null && record.RecordingStoppedAt is null)
                    await recordingController.FinalizeOnDisconnectAsync(new RecordingCommand
                    {
                        ChannelUuid      = sessionUuid,
                        CallRecordId     = record.Id,
                        TenantSchemaName = session.TenantSchemaName,
                        Source           = RecordingEventSource.Disconnect,
                        Reason           = cause,
                    }, ct);

                if (session.CampaignId != Guid.Empty)
                    campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == session.CampaignId, ct);

                // Classify the call's queue-lifecycle outcome for the state history log
                if (assignedAgentIdStr is not null && Guid.TryParse(assignedAgentIdStr, out var completedAgentId))
                {
                    await callStateRecorder.RecordAsync(
                        session.TenantId, session.TenantSchemaName, record.Id,
                        CallHistoryState.Completed, session.CampaignId, completedAgentId, cause, ct: ct);
                }
                else if (session.Vars.ContainsKey("_left_for_callback"))
                {
                    // The caller booked a callback via tf_request_callback and then hung up — this
                    // is neither an abandon nor a completion. The Callback row's own lifecycle
                    // (completed / abandoned / expired) is the authoritative outcome; here we just
                    // close the timeline for this inbound leg.
                    await callStateRecorder.RecordAsync(
                        session.TenantId, session.TenantSchemaName, record.Id,
                        CallHistoryState.PostAgent, session.CampaignId, agentId: null,
                        detail: "Call ended — callback pending", ct: ct);
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

        // Diagnostic snapshot of both legs at the moment of bridge — the agent/caller bridge bug
        // shows CHANNEL_BRIDGE firing against an agent leg with no working media, so capture the
        // answer state + RTP counters for each side here.
        await LogBridgeLegStateAsync(esl, "A/" + uuid, uuid, ct);
        if (!string.IsNullOrEmpty(other))
            await LogBridgeLegStateAsync(esl, "B/" + other, other, ct);

        var bridgeSession = await _sessionStore.GetAsync(uuid, ct);
        if (bridgeSession is null) return;

        // tf_transfer external_number connected — the outbound leg is now bridged to the caller, so
        // the transfer is no longer "in progress". Clearing this lets that outbound leg's eventual
        // CHANNEL_HANGUP complete the call normally instead of being read as a bridge failure.
        if (bridgeSession.Vars.Remove("_xfer_in_progress"))
        {
            foreach (var k in new[] { "_xfer_node_id", "_xfer_next_failed" })
                bridgeSession.Vars.Remove(k);
            await _sessionStore.SaveAsync(bridgeSession, ct);
        }

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

        // Stop any active play loop — the call is now bridged. Clear ALL _play_* (not just the two
        // loop keys) so a periodic-announcement PLAYBACK_STOP that lands right after the bridge
        // can't re-broadcast MOH onto the live agent call, and PlayAnnouncementService (which gates
        // on _play_loop) stops considering this session.
        if (bridgeSession.Vars.ContainsKey("_play_media_arg"))
        {
            ClearPlayVars(bridgeSession);
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

    /// <summary>Diagnostic: snapshot a bridge leg's existence, negotiated codec, and RTP media
    /// counters. Null/absent packet counts (or a channel that no longer exists) are the tell for
    /// the "bridged to a media-less agent leg" bug.</summary>
    private async Task LogBridgeLegStateAsync(EslClient esl, string label, string uuid, CancellationToken ct)
    {
        var exists     = await esl.GetChannelVarAsync(uuid, "uuid", ct);
        var readCodec  = await esl.GetChannelVarAsync(uuid, "read_codec", ct);
        var writeCodec = await esl.GetChannelVarAsync(uuid, "write_codec", ct);
        var rtpCodec   = await esl.GetChannelVarAsync(uuid, "rtp_use_codec_name", ct);
        var rtpIn      = await esl.GetChannelVarAsync(uuid, "rtp_audio_in_packet_count", ct);
        var rtpOut     = await esl.GetChannelVarAsync(uuid, "rtp_audio_out_packet_count", ct);
        _logger.LogInformation(
            "CHANNEL_BRIDGE leg {Label}: exists={Exists} read={ReadCodec} write={WriteCodec} rtpCodec={RtpCodec} rtpInPkts={RtpIn} rtpOutPkts={RtpOut}",
            label, exists is not null, readCodec, writeCodec, rtpCodec, rtpIn, rtpOut);
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

        // Streaming-TTS chunk finished — separate queue-draining path, not the file/flite one below.
        if (session is not null && session.Vars.GetValueOrDefault("_play_stream_now_playing") == "true")
        {
            await HandleStreamChunkFinishedAsync(session, uuid, esl, ct);
            return;
        }

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

        // ── Periodic announcement in progress ──────────────────────────────────
        // PlayAnnouncementService set _play_state="announcement" and fired a uuid_broadcast of the
        // announcement, which interrupts the looping MOH. That produces TWO PLAYBACK_STOP events:
        // first the interrupted MOH, then the announcement itself when it finishes. Only the
        // second one should resume the loop / advance the playlist. Tell them apart by the stopped
        // file path (Playback-File-Path); if the event carried none, fall back to a ~1 s time
        // guard off _play_announcement_fired_at (an announcement never ends that fast).
        if (currentState == "announcement")
        {
            var stoppedFile   = vars.GetValueOrDefault("Playback-File-Path", "");
            var announcements  = GetAnnouncementList(session);

            bool isAnnouncementDone;
            if (!string.IsNullOrEmpty(stoppedFile))
            {
                isAnnouncementDone = announcements.Any(a => PlayFilePathsMatch(a, stoppedFile));
            }
            else
            {
                var firedRecently =
                    DateTimeOffset.TryParse(session.Vars.GetValueOrDefault("_play_announcement_fired_at", ""), out var firedAt)
                    && (DateTimeOffset.UtcNow - firedAt).TotalSeconds < 1.0;
                isAnnouncementDone = !firedRecently;
            }

            if (!isAnnouncementDone)
                return; // this was the MOH interrupt — the announcement is playing now

            if (announcements.Count > 0)
            {
                var currentIdx = int.TryParse(
                    session.Vars.GetValueOrDefault("_play_announcement_index", "0"), out var ci) ? ci : 0;
                session.Vars["_play_announcement_index"] = ((currentIdx + 1) % announcements.Count).ToString();
            }

            session.Vars["_play_state"]                = "main";
            session.Vars["_play_last_announcement_at"] = DateTimeOffset.UtcNow.ToString("O");
            session.Vars.Remove("_play_announcement_fired_at");

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

        // ── Main media finished — loop it, or end. (Periodic-announcement timing is driven by
        //    PlayAnnouncementService, not this boundary — a looping MOH source may never reach it.)
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
        Dictionary<string, string> vars, string? eventBody, EslClient esl, CancellationToken ct)
    {
        switch (vars.GetValueOrDefault("Event-Subclass"))
        {
            case "mod_audio_stream::play":
                await HandleAudioStreamPlayAsync(vars, eventBody, esl, ct);
                break;
            case "mod_audio_stream::disconnect":
                await HandleAudioStreamFinishedAsync(vars, esl, "disconnected", ct);
                break;
            case "mod_audio_stream::error":
                await HandleAudioStreamFinishedAsync(vars, esl, "error", ct);
                break;
            case "mod_audio_stream::connect":
                _logger.LogDebug("AudioStreamConnect [{Uuid}]", vars.GetValueOrDefault("Unique-ID"));
                break;
            case "contactconnection::ivr_done":
                await HandleIvrDoneAsync(vars, esl, ct);
                break;
            case "contactconnection::vm_done":
                await HandleVmDoneAsync(vars, esl, ct);
                break;
            case "contactconnection::xfer_failed":
                await HandleXferFailedAsync(vars, esl, ct);
                break;
        }
    }

    /// <summary>
    /// The xfer_bridge dialplan extension (tf_transfer → external_number) couldn't connect the
    /// bridge and re-parked the caller. Clear the _xfer_* markers and resume the telephony flow on
    /// the node's <c>failed</c> handle so it can offer voicemail / another destination.
    /// </summary>
    private async Task HandleXferFailedAsync(Dictionary<string, string> vars, EslClient esl, CancellationToken ct)
    {
        var uuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(uuid)) return;

        var session = await ResolveSessionAsync(uuid, vars, ct);
        if (session is null || session.Vars.GetValueOrDefault("_xfer_in_progress") != "true") return;

        var nextFailed = session.Vars.GetValueOrDefault("_xfer_next_failed");
        var cause      = vars.GetValueOrDefault("cc_xfer_cause") ?? "unknown";

        foreach (var k in new[] { "_xfer_in_progress", "_xfer_node_id", "_xfer_next_failed" })
            session.Vars.Remove(k);
        await _sessionStore.SaveAsync(session, ct);

        _logger.LogInformation(
            "xfer_failed {Uuid}: external transfer did not connect (cause={Cause}) → {Next}",
            uuid, cause, string.IsNullOrEmpty(nextFailed) ? "(no failed handle — call stays parked)" : nextFailed);

        if (!string.IsNullOrEmpty(nextFailed))
            await ResumeAsync(session.ChannelUuid, nextFailed, esl, ct);
    }

    /// <summary>
    /// mod_audio_stream::play — one decoded audio chunk is ready, written by the module to a temp
    /// file on the FreeSWITCH host (event body: {"audioDataType","sampleRate","file"}). The module
    /// never plays audio itself — every chunk needs an explicit uuid_broadcast from us, chained off
    /// each broadcast's own PLAYBACK_STOP so multiple chunks for one utterance play in sequence
    /// instead of interrupting each other (see HandleStreamChunkFinishedAsync). A chunk that arrives
    /// while nothing is playing starts immediately; one that arrives mid-broadcast is queued.
    /// </summary>
    private async Task HandleAudioStreamPlayAsync(
        Dictionary<string, string> vars, string? eventBody, EslClient esl, CancellationToken ct)
    {
        var uuid = vars.GetValueOrDefault("Unique-ID");
        if (string.IsNullOrEmpty(uuid)) return;

        var session = await _sessionStore.GetAsync(uuid, ct);
        if (session is null || session.Vars.GetValueOrDefault("_play_state") != "streaming") return;

        if (string.IsNullOrWhiteSpace(eventBody))
        {
            _logger.LogWarning("AudioStreamPlay [{Uuid}]: play event had no parsable body — chunk dropped", uuid);
            return;
        }

        string? filePath;
        try
        {
            using var doc = JsonDocument.Parse(eventBody);
            filePath = doc.RootElement.TryGetProperty("file", out var fileEl) ? fileEl.GetString() : null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "AudioStreamPlay [{Uuid}]: could not parse play event body: {Body}", uuid, eventBody);
            return;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("AudioStreamPlay [{Uuid}]: play event body had no 'file': {Body}", uuid, eventBody);
            return;
        }

        if (session.Vars.GetValueOrDefault("_play_stream_now_playing") == "true")
        {
            var pending = GetStreamQueue(session);
            pending.Add(filePath);
            session.Vars["_play_stream_pending_json"] = JsonSerializer.Serialize(pending);
            await _sessionStore.SaveAsync(session, ct);
            _logger.LogInformation("AudioStreamPlay [{Uuid}]: queued chunk {File} ({Count} pending)", uuid, filePath, pending.Count);
            return;
        }

        session.Vars["_play_stream_now_playing"] = "true";
        await _sessionStore.SaveAsync(session, ct);
        _logger.LogInformation("AudioStreamPlay [{Uuid}]: broadcasting chunk {File}", uuid, filePath);
        await esl.BroadcastAsync(uuid, filePath, ct);
    }

    /// <summary>
    /// PLAYBACK_STOP for a streaming-TTS chunk (see HandlePlaybackStopAsync's early branch). Pops
    /// the next queued chunk and plays it; once the queue is empty, either goes idle (more chunks
    /// may still be arriving from the vendor) or — if mod_audio_stream::disconnect/error already
    /// landed — resumes the flow, since this was genuinely the last chunk.
    /// </summary>
    private async Task HandleStreamChunkFinishedAsync(
        TelephonyCallSession session, string uuid, EslClient esl, CancellationToken ct)
    {
        var pending = GetStreamQueue(session);
        if (pending.Count > 0)
        {
            var next = pending[0];
            pending.RemoveAt(0);
            session.Vars["_play_stream_pending_json"] = JsonSerializer.Serialize(pending);
            await _sessionStore.SaveAsync(session, ct);
            _logger.LogInformation(
                "AudioStreamPlay [{Uuid}]: broadcasting queued chunk {File} ({Remaining} left)", uuid, next, pending.Count);
            await esl.BroadcastAsync(uuid, next, ct);
            return;
        }

        session.Vars["_play_stream_now_playing"] = "false";
        if (session.Vars.GetValueOrDefault("_play_stream_disconnected") == "true")
        {
            // "_play_stream_disconnected" is set directly by the relay (TtsStreamRelayEndpoints)
            // the instant it's done forwarding chunks — not by mod_audio_stream's own disconnect
            // event, which only fires *after* we tell it to stop (see TtsPlaybackCoordinator for
            // why that would deadlock). Signal the relay's wait so it can now safely stop the
            // stream — its cleanup deletes every temp file for this session, and every chunk has
            // genuinely finished playing at this point.
            _logger.LogInformation("AudioStreamPlay [{Uuid}]: last queued chunk finished after stream end — resuming flow", uuid);
            _ttsPlaybackCoordinator.SignalDrained(uuid);
            await FireEndTransitionAsync(session, uuid, "tts", esl, ct);
        }
        else
        {
            await _sessionStore.SaveAsync(session, ct);
        }
    }

    private static List<string> GetStreamQueue(TelephonyCallSession session)
    {
        var json = session.Vars.GetValueOrDefault("_play_stream_pending_json", "");
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    /// <summary>
    /// mod_audio_stream::disconnect/::error — the actual FreeSWITCH-side signal that the stream
    /// stopped. In the normal case this fires well *after* the flow has already resumed (the
    /// relay now waits for the local play queue to fully drain — see TtsPlaybackCoordinator —
    /// before it ever calls "uuid_audio_stream stop", and that's what triggers this event), so by
    /// the time it lands here ClearPlayVars has already cleared "_play_state" and the guard below
    /// makes this a no-op. It only actually does something as a safety net: if the relay's
    /// drain-wait timed out (a chunk got stuck, or some other gap) and it stopped anyway, this is
    /// what stops the caller being left on a channel that would otherwise never resume — better a
    /// possibly-truncated resume than a permanently stuck call. ::error is a secondary,
    /// diagnostic-only signal for the same underlying event, guarded against a double resume by
    /// the same "_play_state" check ClearPlayVars leaves behind after the first one lands.
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
        await LogBridgeLegStateAsync(esl, "whisper-agent/" + agentUuid, agentUuid, ct);

        // Remove the reverse mapping — whisper is done
        await _sessionStore.DeleteKeyAsync($"whisper:{agentUuid}", ct);

        var session = await _sessionStore.GetAsync(callerUuid, ct);
        if (session is null) return;

        var nextNodeId = session.Vars.GetValueOrDefault("_whisper_next_default");

        // Clear whisper state vars
        var keysToRemove = session.Vars.Keys.Where(k => k.StartsWith("_whisper_")).ToList();
        foreach (var k in keysToRemove) session.Vars.Remove(k);

        // Also kill the caller's hold-music play-loop state. The caller has been sitting in a
        // tf_play loop (announcement → MOH) while queued; we're about to resume into tf_end,
        // whose uuid_break on the caller leg is indistinguishable from that hold file ending
        // naturally. If _play_* vars survive, HandlePlaybackStopAsync treats that break as an
        // end_of_stream transition and advances the caller flow into the next tf_play —
        // re-broadcasting music-on-hold over the just-bridged agent (intermittently wedging
        // the caller on hold, per Session 94). Clearing here makes that PLAYBACK_STOP a no-op.
        ClearPlayVars(session);
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

    /// <summary>Whether a stored play arg and a PLAYBACK_STOP Playback-File-Path refer to the same
    /// file. FreeSWITCH may report the path with or without leading modifiers (e.g. "@@" offset) or
    /// a slightly different absolute prefix, so match on either being a suffix of the other.</summary>
    private static bool PlayFilePathsMatch(string storedArg, string stoppedFile)
    {
        if (string.IsNullOrEmpty(storedArg) || string.IsNullOrEmpty(stoppedFile)) return false;
        var a = storedArg.Split("@@")[0].TrimEnd();
        return a == stoppedFile
            || a.EndsWith(stoppedFile, StringComparison.OrdinalIgnoreCase)
            || stoppedFile.EndsWith(a, StringComparison.OrdinalIgnoreCase);
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

            // Already finalized — this is the second leg of a bridged call hanging up. The
            // first leg's hangup resolved the session (via Other-Leg-Unique-ID), fired
            // call_disconnected, completed the record, restored agent state, and deleted the
            // session; the safety-net hangup then tore this leg down too. Nothing left to do,
            // and it's not an orphan worth warning about.
            if (record.CallEndAt is not null)
            {
                _logger.LogDebug(
                    "CHANNEL_HANGUP {Uuid} cause={Cause}: CallRecord {RecordId} already finalized by its bridge partner — nothing to do",
                    channelUuid, cause, record.Id);
                return;
            }

            record.Complete();
            await db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "CHANNEL_HANGUP {Uuid} cause={Cause} → CallRecord {RecordId} completed (tenant scan, tenant={Tenant}) — " +
                "no call_disconnected event fired, no agent-state restore, no session cleanup for this uuid",
                channelUuid, cause, record.Id, tenant.Subdomain);
            return;
        }
    }
}
