using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_transfer — hands the caller to another destination and (optionally) falls through to a
/// <c>failed</c> transition when the handoff can't be set up.
///
/// destinationType:
///   campaign_queue  — re-point the call at another campaign and enqueue it there (same parked
///                     channel, same call record). QueuePollingService then rings that campaign's
///                     agents. A <c>screenPopFlowId</c> overrides the script the answering agent gets.
///   agent           — direct-bridge to one agent's SIP extension.
///   telephony_flow  — run a different telephony flow from its entry node on this channel.
///   external_number — uuid_transfer into the <c>xfer_bridge</c> dialplan extension, which bridges
///                     to a PSTN number (via a SIP gateway) or a raw SIP URI. On bridge failure the
///                     extension emits contactconnection::xfer_failed and re-parks so
///                     EslBackgroundService can follow the <c>failed</c> handle.
///
/// Announcement (announceAudioFileId → announceTtsText fallback), for every destination except
/// external_number, is played to the caller <em>foreground</em> in the <c>tts_play</c> dialplan
/// extension. The node returns terminal after firing it and is re-run by
/// EslBackgroundService.HandleTtsDoneAsync on <c>contactconnection::tts_done</c> — deferred
/// continuation, the same shape as tf_ivr_menu / tf_voicemail. external_number plays its
/// announcement inline in the <c>xfer_bridge</c> extension.
/// </summary>
public class TransferNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_transfer";

    private readonly ITenantDbContextFactory _factory;
    private readonly EligibleAgentRanker _ranker;
    private readonly ICallStateHistoryRecorder _callStateRecorder;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly ITtsStreamingService _tts;
    private readonly ITtsFileSynthesizer _fileSynth;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly ILogger<TransferNodeHandler> _logger;

    public TransferNodeHandler(
        ITenantDbContextFactory factory,
        EligibleAgentRanker ranker,
        ICallStateHistoryRecorder callStateRecorder,
        ITelephonyCallSessionStore sessionStore,
        ITtsStreamingService tts,
        ITtsFileSynthesizer fileSynth,
        IServiceProvider services,
        IConfiguration config,
        ILogger<TransferNodeHandler> logger)
    {
        _factory           = factory;
        _ranker            = ranker;
        _callStateRecorder = callStateRecorder;
        _sessionStore      = sessionStore;
        _tts               = tts;
        _fileSynth         = fileSynth;
        _services          = services;
        _config            = config;
        _logger            = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var transitions = node["transitions"]?.AsObject();
        var destType    = node["destinationType"]?.GetValue<string>() ?? "campaign_queue";

        if (ctx.Esl is null)
        {
            _logger.LogWarning("TransferNodeHandler [{Uuid}]: no ESL connection — cannot transfer", ctx.ChannelUuid);
            return Follow(transitions, "failed");
        }

        // Screen-pop override rides along in flow vars; tf_script_pop reads it on agent answer.
        var screenPopFlowId = node["screenPopFlowId"]?.GetValue<string>();
        if (Guid.TryParse(screenPopFlowId, out var spFlow))
            ctx.Vars["_screenpop_flow_override"] = spFlow.ToString();

        return destType switch
        {
            "agent"           => await TransferToAgentAsync(node, ctx, transitions, ct),
            "telephony_flow"  => await TransferToFlowAsync(node, ctx, transitions, ct),
            "external_number" => await TransferToExternalAsync(node, ctx, transitions, ct),
            _                 => await TransferToCampaignQueueAsync(node, ctx, transitions, ct),
        };
    }

    // ── agent ────────────────────────────────────────────────────────────────

    private async Task<TelephonyNodeResult> TransferToAgentAsync(
        JsonObject node, TelephonyFlowContext ctx, JsonObject? transitions, CancellationToken ct)
    {
        var extension = node["agentExtension"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(extension))
        {
            _logger.LogWarning("TransferNodeHandler [{Uuid}]: agent transfer with no extension", ctx.ChannelUuid);
            return Follow(transitions, "failed");
        }

        await using var db = _factory.Create(ctx.TenantSchemaName);
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.SipExtension == extension && a.IsActive, ct);
        if (agent is null)
        {
            _logger.LogWarning(
                "TransferNodeHandler [{Uuid}]: no active agent for extension {Ext}", ctx.ChannelUuid, extension);
            return Follow(transitions, "failed");
        }

        if (await PlayAnnouncementAsync(node, ctx, ct))
            return new TelephonyNodeResult(null, "transferring");

        // Deliver via the same queue path as campaign_queue (single-agent eligible list) rather than
        // an inline bridge — the direct BridgeToAgentAsync races the channel settling right after the
        // ivr/resume transfer (CHAN_NOT_IMPLEMENTED). QueuedCallDeliveryService rings + bridges on
        // its own tick and handles agent_answer / screen-pop / ACW. Stays on the current campaign.
        ctx.Vars["_queued"]          = "true";
        ctx.Vars["_eligible_agents"] = agent.Id.ToString();
        ctx.Vars["_in_queue_at"]     = DateTimeOffset.UtcNow.ToString("O");
        ctx.Vars.Remove("_on_timeout_node_id");

        await _callStateRecorder.RecordAsync(
            ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId,
            CallHistoryState.InQueue, ctx.CampaignId, agentId: null, detail: "transferred-to-agent", ct: ct);

        _logger.LogInformation(
            "TransferNodeHandler [{Uuid}]: transferred to agent {AgentId} (ext {Ext}) via queue delivery",
            ctx.ChannelUuid, agent.Id, extension);

        return Follow(transitions, "transferred");
    }

    // ── telephony_flow ───────────────────────────────────────────────────────

    private async Task<TelephonyNodeResult> TransferToFlowAsync(
        JsonObject node, TelephonyFlowContext ctx, JsonObject? transitions, CancellationToken ct)
    {
        if (!Guid.TryParse(node["targetTelephonyFlowId"]?.GetValue<string>(), out var flowId))
            return Follow(transitions, "failed");

        if (await PlayAnnouncementAsync(node, ctx, ct))
            return new TelephonyNodeResult(null, "transferring");

        // Resolve lazily — the engine depends on the handler set, so constructor injection would cycle.
        var engine = _services.GetRequiredService<ITelephonyFlowEngine>();
        var ok = await engine.SwitchFlowAsync(ctx.ChannelUuid, flowId, ctx.Esl!, ct);

        return Follow(transitions, ok ? "transferred" : "failed");
    }

    // ── external_number ──────────────────────────────────────────────────────

    private async Task<TelephonyNodeResult> TransferToExternalAsync(
        JsonObject node, TelephonyFlowContext ctx, JsonObject? transitions, CancellationToken ct)
    {
        var raw = node["externalNumber"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogWarning("TransferNodeHandler [{Uuid}]: external transfer with no number", ctx.ChannelUuid);
            return Follow(transitions, "failed");
        }

        string dest;
        if (raw.StartsWith("sip:", StringComparison.OrdinalIgnoreCase))
        {
            dest = $"sofia/external/{raw}";
        }
        else
        {
            var gw = node["externalGatewayName"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(gw))
                gw = _config["FreeSWITCH:DefaultGateway"] ?? "telnyx";
            var digits = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());
            dest = $"sofia/gateway/{gw.Trim()}/{digits}";
        }

        var announce = await ResolveAnnouncementArgAsync(node, ctx, ct) ?? "silence_stream://100";

        await ctx.Esl!.SetChannelVarAsync(ctx.ChannelUuid, "cc_xfer_dest", dest, ct);
        await ctx.Esl!.SetChannelVarAsync(ctx.ChannelUuid, "cc_xfer_announce", announce, ct);

        ctx.Vars["_xfer_in_progress"]  = "true";
        ctx.Vars["_xfer_node_id"]      = node["nodeId"]?.GetValue<string>() ?? string.Empty;
        ctx.Vars["_xfer_next_failed"]  = transitions?["failed"]?.GetValue<string>() ?? string.Empty;

        _logger.LogInformation("TransferNodeHandler [{Uuid}]: → xfer_bridge dest={Dest}", ctx.ChannelUuid, dest);
        await ctx.Esl!.TransferAsync(ctx.ChannelUuid, "xfer_bridge", "XML", "default", ct);

        // Terminal — a successful bridge owns the call; a failed one comes back via
        // contactconnection::xfer_failed → EslBackgroundService resumes on _xfer_next_failed.
        return new TelephonyNodeResult(null, "transferring");
    }

    // ── campaign_queue ───────────────────────────────────────────────────────

    private async Task<TelephonyNodeResult> TransferToCampaignQueueAsync(
        JsonObject node, TelephonyFlowContext ctx, JsonObject? transitions, CancellationToken ct)
    {
        if (!Guid.TryParse(node["targetCampaignId"]?.GetValue<string>(), out var targetCampaignId))
        {
            _logger.LogWarning("TransferNodeHandler [{Uuid}]: campaign transfer with no/invalid target campaign", ctx.ChannelUuid);
            return Follow(transitions, "failed");
        }
        if (targetCampaignId == ctx.CampaignId)
            _logger.LogInformation("TransferNodeHandler [{Uuid}]: transfer target is the current campaign", ctx.ChannelUuid);

        await using var db = _factory.Create(ctx.TenantSchemaName);
        var target = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == targetCampaignId, ct);
        if (target is null)
        {
            _logger.LogWarning("TransferNodeHandler [{Uuid}]: target campaign {Campaign} not found", ctx.ChannelUuid, targetCampaignId);
            return Follow(transitions, "failed");
        }

        // Respect the target campaign's queue ceiling.
        if (target.MaxQueueSize > 0)
        {
            var allSessions = await _sessionStore.GetAllAsync(ct);
            var queued = allSessions.Count(
                s => s.CampaignId == targetCampaignId && s.Vars.GetValueOrDefault("_queued") == "true");
            if (queued >= target.MaxQueueSize)
            {
                _logger.LogWarning(
                    "TransferNodeHandler [{Uuid}]: target campaign {Campaign} queue full ({Queued}/{Max})",
                    ctx.ChannelUuid, targetCampaignId, queued, target.MaxQueueSize);
                return Follow(transitions, "failed");
            }
        }

        if (await PlayAnnouncementAsync(node, ctx, ct))
            return new TelephonyNodeResult(null, "transferring");

        var ranked = await _ranker.GetRankedEligibleAgentsAsync(db, ctx.TenantId, targetCampaignId, ct: ct);
        var eligible = target.RingStrategy == CampaignRingStrategy.RingTopNByProficiency
            ? ranked.Take(target.RingTopN)
            : ranked;
        var eligibleIds = eligible.Select(r => r.AgentId).ToList();

        // Move the call record onto the target campaign, and signal the engine to update the
        // Redis session's CampaignId (a handler can't persist that itself — see
        // TelephonyFlowEngine.ApplyPendingSessionMutations).
        var record = await db.CallRecords.FirstOrDefaultAsync(r => r.Id == ctx.CallRecordId, ct);
        record?.SetCampaign(targetCampaignId);
        if (record is not null) await db.SaveChangesAsync(ct);

        ctx.Vars["_switch_campaign_id"] = targetCampaignId.ToString();
        ctx.Vars["_queued"]            = "true";
        ctx.Vars["_eligible_agents"]   = string.Join(",", eligibleIds);
        ctx.Vars["_in_queue_at"]       = DateTimeOffset.UtcNow.ToString("O");
        // A prior queue's timeout target no longer applies to the new campaign.
        ctx.Vars.Remove("_on_timeout_node_id");

        await _callStateRecorder.RecordAsync(
            ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId,
            CallHistoryState.InQueue, targetCampaignId, agentId: null, detail: "transferred", ct: ct);

        _logger.LogInformation(
            "TransferNodeHandler [{Uuid}]: transferred to campaign {Campaign} queue ({Count} eligible agent(s))",
            ctx.ChannelUuid, targetCampaignId, eligibleIds.Count);

        return Follow(transitions, "transferred");
    }

    // ── announcement ─────────────────────────────────────────────────────────

    /// <summary>
    /// Deferred-continuation announcement for every destination except external_number. First pass:
    /// resolve the configured announcement (audio file → streaming vendor → flite, first match
    /// wins), <c>uuid_transfer</c> the caller into the <c>tts_play</c> extension for a foreground
    /// playback, and return <c>true</c> — the caller must return a terminal result.
    /// EslBackgroundService.HandleTtsDoneAsync re-runs this node on
    /// <c>contactconnection::tts_done</c>, this time with <c>_announce_done</c> set, so the second
    /// pass returns <c>false</c> and the handoff proceeds. Nothing configured also returns
    /// <c>false</c> (fire-immediately).
    ///
    /// A shout:// announcement can't be fire-and-forget (mod_shout never fires PLAYBACK_STOP on a
    /// finite stream), and blocking the handler while awaiting the event proved unreliable — the
    /// flow engine holds a stale session for the whole wait. Deferred continuation avoids both.
    /// </summary>
    /// <returns><c>true</c> when the announcement was fired and the caller must return terminal;
    /// <c>false</c> to proceed with the handoff (nothing to play, or the post-announcement pass).</returns>
    private async Task<bool> PlayAnnouncementAsync(JsonObject node, TelephonyFlowContext ctx, CancellationToken ct)
    {
        if (ctx.Esl is null) return false;

        // Second pass — tts_done resumed us here after the announcement finished.
        if (ctx.Vars.ContainsKey("_announce_done"))
        {
            ctx.RemoveSessionVar("_announce_done");
            ctx.RemoveSessionVar("_announce_replay_node");
            return false;
        }

        var announceArg = await ResolveLiveAnnouncementArgAsync(node, ctx, ct);
        if (announceArg is null) return false;   // nothing configured → immediate handoff

        var replayNode = node["nodeId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(replayNode))
        {
            // No node id to come back to — play it fire-and-forget and proceed (best effort).
            _logger.LogWarning(
                "TransferNodeHandler [{Uuid}]: node has no id — announcement played fire-and-forget", ctx.ChannelUuid);
            await ctx.Esl.BroadcastAsync(ctx.ChannelUuid, announceArg, ct);
            return false;
        }

        // Markers go in ctx.Vars so the flow engine persists them — HandleTtsDoneAsync and the
        // CHANNEL_PARK / PLAYBACK_STOP guards read them off the session.
        ctx.Vars["_announce_in_progress"] = "true";
        ctx.Vars["_announce_replay_node"] = replayNode;
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_tts_url", announceArg, ct);
        await ctx.Esl.TransferAsync(ctx.ChannelUuid, "tts_play", "XML", "default", ct);
        _logger.LogInformation(
            "TransferNodeHandler [{Uuid}]: announcement → tts_play ({Arg}); deferring handoff to tts_done",
            ctx.ChannelUuid, announceArg);
        return true;
    }

    /// <summary>
    /// Resolves the live-channel announcement to a single <c>playback</c>-ready media arg: a tenant
    /// audio file, else a streaming-vendor shout:// URL, else a flite tts string (with the text
    /// routed through <c>cc_xfer_announce_text</c> to keep the arg space-free). Null when the node
    /// has no announcement configured.
    /// </summary>
    private async Task<string?> ResolveLiveAnnouncementArgAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct)
    {
        var fileArg = await TelephonyAudioResolver.ResolveFileArgAsync(
            _factory, _config, node["announceAudioFileId"]?.GetValue<string>(), ctx.TenantSchemaName, ct);
        if (fileArg is not null) return fileArg;

        var tts = node["announceTtsText"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(tts)) return null;
        var voice = node["announceTtsVoice"]?.GetValue<string>() ?? "kal";

        var provider = await _tts.ResolveProviderAsync(ctx.TenantSchemaName, ct);
        if (provider is not null)
            return await _tts.PrepareStreamUrlAsync(ctx.TenantSubdomain, provider, tts, voice, ct);

        await ctx.Esl!.SetChannelVarAsync(ctx.ChannelUuid, "cc_xfer_announce_text", tts.Replace("\n", " ").Trim(), ct);
        return $"tts://flite|{voice}|${{cc_xfer_announce_text}}";
    }

    /// <summary>
    /// Used only by external_number — the announcement plays inline inside the xfer_bridge
    /// dialplan extension (uuid_transfer, not a live broadcast we control), so it must resolve to
    /// a static, playable media-arg string. Live vendor streaming can't be inlined there (same
    /// constraint as tf_voicemail's greeting) — pre-synthesize to a cached file instead.
    /// </summary>
    private async Task<string?> ResolveAnnouncementArgAsync(JsonObject node, TelephonyFlowContext ctx, CancellationToken ct)
    {
        var fileArg = await TelephonyAudioResolver.ResolveFileArgAsync(
            _factory, _config, node["announceAudioFileId"]?.GetValue<string>(), ctx.TenantSchemaName, ct);
        if (fileArg is not null) return fileArg;

        var tts = node["announceTtsText"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(tts)) return null;
        var voice = node["announceTtsVoice"]?.GetValue<string>() ?? "kal";

        var provider = await _tts.ResolveProviderAsync(ctx.TenantSchemaName, ct);
        if (provider is not null)
        {
            var cachedArg = await _fileSynth.SynthesizeToFileAsync(
                ctx.TenantSchemaName, ctx.TenantSubdomain, provider.ProviderKey, provider.SettingsJson, voice, tts, ct);
            if (cachedArg is not null) return cachedArg;
            // Synthesis failed (missing credential, vendor error, …) — fall through to flite.
        }

        await ctx.Esl!.SetChannelVarAsync(ctx.ChannelUuid, "cc_xfer_announce_text", tts.Replace("\n", " ").Trim(), ct);
        return $"tts://flite|{voice}|${{cc_xfer_announce_text}}";
    }

    private static TelephonyNodeResult Follow(JsonObject? transitions, string key)
    {
        var target = transitions?[key]?.GetValue<string>() ?? transitions?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(target, key);
    }
}
