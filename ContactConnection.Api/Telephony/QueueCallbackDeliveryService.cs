using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// The "virtual hold" delivery path for tf_queue_callback placeholders. Distinct from
/// <see cref="QueuedCallDeliveryService"/> (which bridges a caller who is still on the line):
/// here the caller has hung up and only the placeholder session remains.
///
///   1. <see cref="ReserveAndDialAsync"/> — QueuePollingService picked an available agent for a
///      placeholder. Reserve that agent (<see cref="AgentStateCodes.CallbackPending"/> — the
///      ranker then skips them), then originate an outbound &amp;park() leg to the caller's
///      number carrying cc_qcb_* channel vars.
///   2. <see cref="ConnectAnsweredLegAsync"/> — that leg answered and parked. Re-key the
///      placeholder session onto the new channel, point the original CallRecord at it, play a
///      connect prompt, then hand off to <see cref="QueuedCallDeliveryService.DeliverAsync"/>
///      for the reserved agent (normal whisper / script-pop choreography, agent → OnCall).
///   3. <see cref="HandleFailedLegAsync"/> — that leg never answered (no answer / busy /
///      rejected / originate timeout). Release the agent; retry the placeholder while attempts
///      remain, else record a callback abandon and drop the placeholder.
/// </summary>
public sealed class QueueCallbackDeliveryService(
    ITenantDbContextFactory dbFactory,
    ITelephonyCallSessionStore sessionStore,
    IAgentStateStore stateStore,
    IHubContext<FlowHub, IFlowHubClient> hub,
    ICallStateHistoryRecorder callStateRecorder,
    QueuedCallDeliveryService queuedCallDelivery,
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<QueueCallbackDeliveryService> logger,
    ILogger<EslClient> eslLogger)
{
    private const string DefaultConnectPrompt = "ivr/ivr-hold_connect_call.wav";
    private const int RetryCooloffSeconds = 60;

    // How long to let the connect prompt play before handing to delivery — the simple-bridge path
    // bridges instantly and would otherwise cut it off. Off the ESL loop, so blocking is fine.
    private int ConnectPromptSettleMs =>
        int.TryParse(config["FreeSWITCH:QueueCallback:ConnectPromptSettleMs"], out var m) && m >= 0 ? m : 3000;
    // Bridge failures after the caller already answered → re-queue rather than abandon, up to this
    // many times (then abandon, to bound an endless loop against a genuinely broken softphone).
    private int MaxBridgeRetries =>
        int.TryParse(config["FreeSWITCH:QueueCallback:MaxBridgeRetries"], out var n) && n >= 0 ? n : 2;

    private string EslHost => config["FreeSWITCH:Host"] ?? "127.0.0.1";
    private int    EslPort => int.TryParse(config["FreeSWITCH:EslPort"], out var p) ? p : 8021;
    private string EslPass => config["FreeSWITCH:EslPassword"] ?? "ClueCon";
    private string Gateway => config["FreeSWITCH:DefaultGateway"] ?? "telnyx";

    // ── 1. Reserve an agent + dial the caller back ───────────────────────────────

    public async Task<DeliveryResult> ReserveAndDialAsync(
        TelephonyCallSession placeholder, Guid tenantId, string tenantSchema, string tenantSubdomain,
        Guid agentId, CancellationToken ct)
    {
        var number = placeholder.Vars.GetValueOrDefault("_queue_callback_number");
        if (string.IsNullOrWhiteSpace(number))
            return new DeliveryResult(false, "Placeholder has no callback number.");

        var dnis = placeholder.DestinationNumber;               // the DID the caller originally dialed
        var digits = new string(number.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (digits.Length < 7)
            return new DeliveryResult(false, $"Callback number '{number}' is not dialable.");

        var attempts = ParseInt(placeholder.Vars.GetValueOrDefault("_queue_callback_attempts")) + 1;

        // Reserve the agent BEFORE dialing so no other poll tick / instance routes them a call.
        await stateStore.SetAsync(tenantId, agentId, tenantSchema,
            new AgentStateEntry(AgentStateCodes.CallbackPending, "Callback Pending", null, DateTimeOffset.UtcNow), ct);
        await hub.Clients.Group($"agent:{agentId}")
            .ReceiveAgentStateChange(AgentStateCodes.CallbackPending, "Callback Pending", null);

        placeholder.Vars["_queue_callback_reserved_agent_id"] = agentId.ToString();
        placeholder.Vars["_queue_callback_attempts"]          = attempts.ToString();
        placeholder.Vars.Remove("_queued"); // out of the deliverable set while the dial is in flight
        await sessionStore.SaveAsync(placeholder, ct);

        var command =
            $"originate {{origination_caller_id_number={dnis}," +
            $"cc_did={dnis}," +
            $"cc_qcb_reserved_agent_id={agentId}," +
            $"cc_qcb_placeholder_uuid={placeholder.ChannelUuid}," +
            $"cc_tenant_id={tenantId},cc_tenant_schema={tenantSchema},cc_tenant_subdomain={tenantSubdomain}," +
            $"cc_campaign_id={placeholder.CampaignId}," +
            $"ignore_early_media=true,originate_timeout=30}}" +
            $"sofia/gateway/{Gateway}/{digits} &park()";

        try
        {
            await using var esl = new EslClient(eslLogger);
            await esl.ConnectAsync(EslHost, EslPort, EslPass, ct);
            await esl.SendBgApiAsync(command, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "QueueCallback {Uuid}: originate to {Number} failed to send — releasing agent {AgentId}",
                placeholder.ChannelUuid, number, agentId);
            await ReleaseAgentAsync(tenantId, tenantSchema, agentId, ct);
            placeholder.Vars.Remove("_queue_callback_reserved_agent_id");
            placeholder.Vars["_queued"] = "true";
            await sessionStore.SaveAsync(placeholder, ct);
            return new DeliveryResult(false, "Originate command failed to send.");
        }

        logger.LogInformation(
            "QueueCallback {Uuid} attempt {Attempt}: agent {AgentId} reserved, dialing {Number} (DID {Did})",
            placeholder.ChannelUuid, attempts, agentId, number, dnis);
        return new DeliveryResult(true, null);
    }

    // ── 2. The callback leg answered — bridge it to the reserved agent ───────────

    /// <summary>Returns true if this park was a queue-callback leg and was handled here (the
    /// caller of HandleDidCallAsync must then stop — no inbound flow, no new call record).</summary>
    public async Task<bool> ConnectAnsweredLegAsync(
        string newChannelUuid, IReadOnlyDictionary<string, string> eventVars, EslClient esl, CancellationToken ct)
    {
        if (!Guid.TryParse(eventVars.GetValueOrDefault("variable_cc_qcb_reserved_agent_id"), out var agentId))
            return false;

        // Idempotency: a second CHANNEL_PARK for this leg after the first was already re-keyed
        // into a normal in-flight session — swallow it, don't re-run delivery / hang up a live call.
        var existing = await sessionStore.GetAsync(newChannelUuid, ct);
        if (existing is not null && existing.Vars.GetValueOrDefault("_queue_callback") != "true")
        {
            logger.LogDebug("QueueCallback: park for {Uuid} already handled — ignoring", newChannelUuid);
            return true;
        }

        var placeholderUuid = eventVars.GetValueOrDefault("variable_cc_qcb_placeholder_uuid") ?? "";
        var tenantSchema    = eventVars.GetValueOrDefault("variable_cc_tenant_schema") ?? "";
        var tenantSubdomain = eventVars.GetValueOrDefault("variable_cc_tenant_subdomain") ?? "";
        Guid.TryParse(eventVars.GetValueOrDefault("variable_cc_tenant_id"), out var tenantId);

        var placeholder = string.IsNullOrEmpty(placeholderUuid)
            ? null
            : await sessionStore.GetAsync(placeholderUuid, ct);

        if (placeholder is null || string.IsNullOrEmpty(tenantSchema))
        {
            logger.LogWarning(
                "QueueCallback: answered leg {Uuid} but placeholder {Placeholder} is gone — hanging up + releasing agent {AgentId}",
                newChannelUuid, placeholderUuid, agentId);
            if (tenantId != Guid.Empty && !string.IsNullOrEmpty(tenantSchema))
                await ReleaseAgentAsync(tenantId, tenantSchema, agentId, ct);
            await esl.HangupChannelAsync(newChannelUuid, ct);
            return true;
        }

        await using var db = dbFactory.Create(tenantSchema);
        var record = await db.CallRecords.FirstOrDefaultAsync(r => r.Id == placeholder.CallRecordId, ct);
        if (record is null)
        {
            logger.LogWarning(
                "QueueCallback: answered leg {Uuid} — original call record {RecordId} not found; hanging up",
                newChannelUuid, placeholder.CallRecordId);
            await ReleaseAgentAsync(tenantId, tenantSchema, agentId, ct);
            await esl.HangupChannelAsync(newChannelUuid, ct);
            await sessionStore.DeleteAsync(placeholderUuid, ct);
            return true;
        }

        // Re-key the placeholder session onto the live callback channel and shed the placeholder
        // markers — from here it is an ordinary in-flight call session.
        var connectAudio = placeholder.Vars.GetValueOrDefault("_queue_callback_connect_audio") ?? "";
        // Kept (not stripped): needed if the bridge to the reserved agent then fails — the caller
        // is re-queued and the flow resumes on this node so the tenant's queue-MOH wiring runs.
        var failedNode = placeholder.Vars.GetValueOrDefault("_queue_callback_failed_node") ?? "";
        placeholder.ChannelUuid = newChannelUuid;
        foreach (var k in new[]
        {
            "_queued", "_left_for_callback", "_queue_callback", "_queue_callback_number",
            "_queue_callback_max_attempts", "_queue_callback_attempts", "_queue_callback_connect_audio",
            "_queue_callback_reserved_agent_id", "_queue_callback_retry_after",
        })
            placeholder.Vars.Remove(k);
        await sessionStore.SaveAsync(placeholder, ct);
        if (!string.Equals(placeholderUuid, newChannelUuid, StringComparison.Ordinal))
            await sessionStore.DeleteAsync(placeholderUuid, ct);

        record.SetContactIdExternal(newChannelUuid);
        await db.SaveChangesAsync(ct);

        // The connect prompt is played (and waited on) in BridgeToReservedAgentAsync, off the ESL
        // loop — playing it here + returning would let the simple-bridge delivery path bridge over
        // the top of it.
        var mediaArg = await ResolveConnectMediaAsync(connectAudio, tenantSchema, ct);

        // Honor the campaign's agent answer mode. For AutoAnswerBestAgent the reserved agent's
        // softphone must auto-answer the bridge INVITE with no click — mirror the normal auto-
        // answer delivery path (QueuePollingService.TryAutoAnswerDeliverAsync): push
        // ReceiveAutoConnecting BEFORE DeliverAsync originates so the client arms auto-answer
        // ahead of the INVITE, and carry the original caller's ANI so the agent UI shows the
        // caller's number rather than the bogus From on the outbound-to-cell leg.
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == record.CampaignId, ct);
        var autoAnswer = campaign?.RingStrategy == CampaignRingStrategy.AutoAnswerBestAgent;
        var ani = record.CallerId is { Length: > 0 } cid ? cid : placeholder.CallerNumber;
        if (autoAnswer)
        {
            await hub.Clients.Group($"agent:{agentId}").ReceiveAutoConnecting(
                record.Id.ToString(), ani, ani, placeholder.DestinationNumber, record.CampaignId.ToString());
        }

        logger.LogInformation(
            "QueueCallback: caller answered on {Uuid} (record {RecordId}) — bridging to reserved agent {AgentId} (autoAnswer={AutoAnswer}, ani={Ani})",
            newChannelUuid, record.Id, agentId, autoAnswer, ani);

        // Hand off to the reserved agent OFF the ESL event loop. DeliverAsync originates the
        // agent's softphone and can block the caller ~30s (originate_timeout) if it's unreachable
        // — doing that here would freeze the whole ESL read loop. Fire-and-forget with its own DI
        // scope (this method's scope dies when the CHANNEL_PARK handler returns).
        _ = Task.Run(() => BridgeToReservedAgentAsync(
            record.Id, record.CampaignId, tenantId, tenantSchema, tenantSubdomain, agentId,
            newChannelUuid, mediaArg, failedNode, autoAnswer));

        return true;
    }

    /// <summary>
    /// Runs off the ESL event loop (see <see cref="ConnectAnsweredLegAsync"/>). Plays the connect
    /// prompt and waits for it (the simple-bridge delivery path bridges instantly and would cut it
    /// off), then hands to <see cref="QueuedCallDeliveryService.DeliverAsync"/>. Resolves scoped
    /// services from a fresh scope (the caller's is gone) and uses its own short-lived ESL
    /// connection rather than the shared read-loop socket.
    ///
    /// On bridge failure the caller is still on the line, so — up to <see cref="MaxBridgeRetries"/>
    /// times — they're put back in the queue AND the telephony flow is resumed on the
    /// tf_queue_callback node's <c>failed</c> branch (<paramref name="failedNode"/>), so the tenant's
    /// queue-MOH / alternate-destination wiring gives the caller real audio and a real path. The
    /// agent that just failed is excluded from the re-delivery so a broken softphone isn't picked
    /// again. Only after the bound is exhausted (or no <c>failed</c> branch is wired) is it a
    /// callback abandon.
    /// </summary>
    private async Task BridgeToReservedAgentAsync(
        Guid recordId, Guid campaignId, Guid tenantId, string tenantSchema, string tenantSubdomain,
        Guid agentId, string channelUuid, string connectMediaArg, string failedNode, bool autoAnswer)
    {
        try
        {
            await using var esl = new EslClient(eslLogger);
            await esl.ConnectAsync(EslHost, EslPort, EslPass, CancellationToken.None);

            // Connect prompt to the caller, then let it play out before delivery bridges.
            try { await esl.BroadcastAsync(channelUuid, connectMediaArg, CancellationToken.None); }
            catch (Exception ex) { logger.LogDebug(ex, "QueueCallback {Uuid}: connect prompt broadcast failed (non-fatal)", channelUuid); }
            if (ConnectPromptSettleMs > 0)
                await Task.Delay(ConnectPromptSettleMs, CancellationToken.None);

            using var scope = scopeFactory.CreateScope();
            var delivery = scope.ServiceProvider.GetRequiredService<QueuedCallDeliveryService>();

            DeliveryResult result;
            try
            {
                result = await delivery.DeliverAsync(
                    tenantId, tenantSchema, tenantSubdomain, recordId, agentId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The simple-bridge path throws (not returns) when the agent's softphone can't be
                // resolved — normalise it to a failed result so the re-queue / abandon path runs.
                logger.LogWarning(ex, "QueueCallback: DeliverAsync threw for {RecordId} — treating as bridge failure", recordId);
                result = new DeliveryResult(false, ex.Message);
            }
            if (result.Success) return;

            if (autoAnswer)
                await hub.Clients.Group($"agent:{agentId}").ReceiveAutoConnectFailed(recordId.ToString());
            await ReleaseAgentAsync(tenantId, tenantSchema, agentId, CancellationToken.None);

            var recorder = scope.ServiceProvider.GetRequiredService<ICallStateHistoryRecorder>();
            var session  = await sessionStore.GetAsync(channelUuid, CancellationToken.None);
            var fails    = ParseInt(session?.Vars.GetValueOrDefault("_qcb_bridge_fails")) + 1;

            if (session is not null && fails <= MaxBridgeRetries && !string.IsNullOrEmpty(failedNode))
            {
                // Caller is still connected. Re-queue them AND resume the flow on the
                // tf_queue_callback node's `failed` branch so the tenant's queue-MOH / alternate
                // path runs — real audio, not dead air. Exclude the agent that just failed so
                // QueuePollingService's re-delivery doesn't immediately pick the same broken
                // softphone; QueuePollingService bounds the total re-delivery misses after this.
                logger.LogWarning(
                    "QueueCallback: bridge to reserved agent {AgentId} failed for {RecordId} ({Error}) — re-queuing + resuming `failed` branch {Node} (attempt {Fails}/{Max})",
                    agentId, recordId, result.ErrorDetail, failedNode, fails, MaxBridgeRetries);

                session.Vars["_queued"]           = "true";
                session.Vars["_in_queue_at"]      = DateTimeOffset.UtcNow.ToString("O");
                session.Vars["_qcb_bridge_fails"] = fails.ToString();
                AppendExcludedAgent(session, agentId);
                session.Vars.Remove("_eligible_agents");   // QueuePollingService re-ranks
                session.Vars.Remove("_assigned_agent_id");
                session.Vars.Remove("_pending_agent_id");
                session.Vars.Remove("_pending_interaction_id");
                session.Vars.Remove("_agent_uuid");
                await sessionStore.SaveAsync(session, CancellationToken.None);

                try
                {
                    await scope.ServiceProvider.GetRequiredService<ITelephonyFlowEngine>()
                        .ResumeFromNodeAsync(channelUuid, failedNode, esl, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "QueueCallback {Uuid}: resume of `failed` branch {Node} threw (non-fatal — caller still re-queued)",
                        channelUuid, failedNode);
                }

                await recorder.RecordAsync(
                    tenantId, tenantSchema, recordId, CallHistoryState.InQueue, campaignId,
                    agentId: null, detail: $"Re-queued after callback bridge failure #{fails} (agent {agentId} excluded)",
                    ct: CancellationToken.None);
                return;
            }

            logger.LogWarning(
                "QueueCallback: bridge to reserved agent {AgentId} failed for {RecordId} ({Error}) — {Reason}, hanging up caller + callback abandon",
                agentId, recordId, result.ErrorDetail,
                string.IsNullOrEmpty(failedNode) ? "no `failed` branch wired" : "retry bound exhausted");

            await esl.HangupChannelAsync(channelUuid, CancellationToken.None);
            await recorder.RecordAsync(
                tenantId, tenantSchema, recordId, CallHistoryState.Abandoned, campaignId,
                agentId: null, detail: "Queue callback connected but agent bridge failed",
                abandonType: CallAbandonType.CallbackAbandon, ct: CancellationToken.None);

            await using var db = dbFactory.Create(tenantSchema);
            var record = await db.CallRecords.FirstOrDefaultAsync(r => r.Id == recordId);
            if (record is not null)
            {
                record.Complete();
                await db.SaveChangesAsync();
            }
            await sessionStore.DeleteAsync(channelUuid, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "QueueCallback: background bridge to reserved agent {AgentId} for record {RecordId} threw",
                agentId, recordId);
        }
    }

    // ── 3. The callback leg never answered ──────────────────────────────────────

    /// <summary>Returns true if this hangup was a queue-callback dial leg and was handled here.</summary>
    public async Task<bool> HandleFailedLegAsync(
        IReadOnlyDictionary<string, string> eventVars, string cause, CancellationToken ct)
    {
        if (!Guid.TryParse(eventVars.GetValueOrDefault("variable_cc_qcb_reserved_agent_id"), out var agentId))
            return false;

        var placeholderUuid = eventVars.GetValueOrDefault("variable_cc_qcb_placeholder_uuid") ?? "";
        var tenantSchema    = eventVars.GetValueOrDefault("variable_cc_tenant_schema") ?? "";
        Guid.TryParse(eventVars.GetValueOrDefault("variable_cc_tenant_id"), out var tenantId);

        if (tenantId == Guid.Empty || string.IsNullOrEmpty(tenantSchema)) return true;

        await ReleaseAgentAsync(tenantId, tenantSchema, agentId, ct);

        var placeholder = string.IsNullOrEmpty(placeholderUuid)
            ? null
            : await sessionStore.GetAsync(placeholderUuid, ct);
        if (placeholder is null)
        {
            logger.LogInformation(
                "QueueCallback: dial leg failed (cause={Cause}) — agent {AgentId} released; placeholder already gone", cause, agentId);
            return true;
        }

        var attempts = ParseInt(placeholder.Vars.GetValueOrDefault("_queue_callback_attempts"));
        var maxAttempts = Math.Max(1, ParseInt(placeholder.Vars.GetValueOrDefault("_queue_callback_max_attempts")));
        placeholder.Vars.Remove("_queue_callback_reserved_agent_id");

        if (attempts < maxAttempts)
        {
            // Back into the deliverable set, but not instantly — a 60s cool-off so a no-answer
            // doesn't re-dial the caller on the very next poll tick.
            placeholder.Vars["_queued"] = "true";
            placeholder.Vars["_queue_callback_retry_after"] = DateTimeOffset.UtcNow.AddSeconds(RetryCooloffSeconds).ToString("O");
            await sessionStore.SaveAsync(placeholder, ct);
            logger.LogInformation(
                "QueueCallback {Uuid}: dial attempt {Attempt}/{Max} failed (cause={Cause}) — re-queued, retry after {Cooloff}s",
                placeholder.ChannelUuid, attempts, maxAttempts, cause, RetryCooloffSeconds);
            return true;
        }

        // Out of attempts — a callback abandon against the original inbound call record.
        await using var db = dbFactory.Create(tenantSchema);
        var record = await db.CallRecords.FirstOrDefaultAsync(r => r.Id == placeholder.CallRecordId, ct);
        if (record is not null)
        {
            await callStateRecorder.RecordAsync(
                tenantId, tenantSchema, record.Id, CallHistoryState.Abandoned, placeholder.CampaignId,
                agentId: null, detail: $"Queue callback abandoned after {attempts} attempt(s) (last cause {cause})",
                abandonType: CallAbandonType.CallbackAbandon, ct: ct);
            record.Complete();
            await db.SaveChangesAsync(ct);
        }
        await sessionStore.DeleteAsync(placeholder.ChannelUuid, ct);

        logger.LogInformation(
            "QueueCallback {Uuid}: abandoned after {Attempt} attempt(s) (cause={Cause})",
            placeholder.ChannelUuid, attempts, cause);
        return true;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task ReleaseAgentAsync(Guid tenantId, string tenantSchema, Guid agentId, CancellationToken ct)
    {
        var current = await stateStore.GetAsync(tenantId, agentId, ct);
        if (current?.Code != AgentStateCodes.CallbackPending) return; // agent already moved on
        await stateStore.SetAsync(tenantId, agentId, tenantSchema,
            new AgentStateEntry(AgentStateCodes.Available, "Available", null, DateTimeOffset.UtcNow), ct);
        await hub.Clients.Group($"agent:{agentId}")
            .ReceiveAgentStateChange(AgentStateCodes.Available, "Available", null);
    }

    /// <summary>
    /// Resolve the designer's connect-prompt audio ref to a FreeSWITCH-playable arg via the shared
    /// <see cref="TelephonyAudioResolver"/> — same handling as Play / Whisper / Transfer, so a file
    /// GUID, <c>__builtin:</c>, <c>__platform:</c> phrase, or stream URI all work. Anything blank or
    /// unresolvable falls back to the built-in "please hold, connecting you" prompt.
    /// </summary>
    private async Task<string> ResolveConnectMediaAsync(string connectAudio, string tenantSchema, CancellationToken ct)
    {
        var resolved = await TelephonyAudioResolver.ResolveFileArgAsync(
            dbFactory, config, connectAudio, tenantSchema, ct);
        return resolved ?? DefaultConnectPrompt;
    }

    private static int ParseInt(string? s) => int.TryParse(s, out var n) ? n : 0;

    /// <summary>
    /// Add an agent id to the session's <c>_qcb_excluded_agents</c> CSV — QueuePollingService unions
    /// this into the eligible-agent ranker's exclusion set for a re-queued queue-callback caller, so
    /// the softphone that just failed the bridge isn't handed the call again on the next tick.
    /// </summary>
    private static void AppendExcludedAgent(TelephonyCallSession session, Guid agentId)
    {
        var current = session.Vars.GetValueOrDefault("_qcb_excluded_agents") ?? "";
        var set = current
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        set.Add(agentId.ToString());
        session.Vars["_qcb_excluded_agents"] = string.Join(',', set);
    }
}
