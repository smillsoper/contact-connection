using ContactConnection.Api.Hubs;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

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
    IConfiguration config,
    ILogger<QueueCallbackDeliveryService> logger,
    ILogger<EslClient> eslLogger)
{
    private const string DefaultConnectPrompt = "ivr/ivr-hold_connect_call.wav";
    private const int RetryCooloffSeconds = 60;

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

        // Connect prompt to the caller, then hand to the normal delivery path for the reserved agent.
        var mediaArg = await ResolveConnectMediaAsync(connectAudio, tenantSchema, ct);
        try { await esl.BroadcastAsync(newChannelUuid, $"{mediaArg} aleg", ct); }
        catch (Exception ex) { logger.LogDebug(ex, "QueueCallback {Uuid}: connect prompt broadcast failed (non-fatal)", newChannelUuid); }

        logger.LogInformation(
            "QueueCallback: caller answered on {Uuid} (record {RecordId}) — bridging to reserved agent {AgentId}",
            newChannelUuid, record.Id, agentId);

        var result = await queuedCallDelivery.DeliverAsync(
            tenantId, tenantSchema, tenantSubdomain, record.Id, agentId, ct);

        if (!result.Success)
        {
            logger.LogWarning(
                "QueueCallback: delivery to reserved agent {AgentId} failed for {RecordId}: {Error} — releasing agent, hanging up caller",
                agentId, record.Id, result.ErrorDetail);
            await ReleaseAgentAsync(tenantId, tenantSchema, agentId, ct);
            await esl.HangupChannelAsync(newChannelUuid, ct);
            await callStateRecorder.RecordAsync(
                tenantId, tenantSchema, record.Id, CallHistoryState.Abandoned, record.CampaignId,
                agentId: null, detail: "Queue callback connected but agent bridge failed",
                abandonType: CallAbandonType.CallbackAbandon, ct: ct);
            record.Complete();
            await db.SaveChangesAsync(ct);
            await sessionStore.DeleteAsync(newChannelUuid, ct);
        }

        return true;
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

    private async Task<string> ResolveConnectMediaAsync(string connectAudio, string tenantSchema, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectAudio)) return DefaultConnectPrompt;
        if (connectAudio.StartsWith("__builtin:")) return connectAudio["__builtin:".Length..];
        if (connectAudio.Contains("://") || connectAudio.Contains('/')) return connectAudio;
        if (!Guid.TryParse(connectAudio, out var fileId)) return DefaultConnectPrompt;

        await using var db = dbFactory.Create(tenantSchema);
        var file = await db.AudioFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return DefaultConnectPrompt;

        var containerBase = config["FreeSWITCH:SoundsContainerPath"]
            ?? "/usr/share/freeswitch/sounds/contactconnection";
        return $"{containerBase}/{tenantSchema}/{file.StoredFileName}";
    }

    private static int ParseInt(string? s) => int.TryParse(s, out var n) ? n : 0;
}
