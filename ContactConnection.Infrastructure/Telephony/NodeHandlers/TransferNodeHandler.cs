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
///                     agents. A <c>screenPopFlowId</c> overrides the script the answering agent gets;
///                     otherwise the target campaign's script (not the dialed number's).
///                     <b>Mid-call (S178):</b> when an agent is already on the call (the branch was fired
///                     from the agent's CRM script via trigger_telephony_event), this is a cold transfer:
///                     the caller goes to hold music (park_with_moh), the agent's leg is dropped (they go to
///                     ACW in EslBackgroundService), and the caller is queued for the target campaign. The call
///                     record keeps its campaign, agent and routing tier (sales attribution, commissions, media);
///                     the answering agent gets their own interaction. Event branches run without an ESL
///                     connection, so one is opened for this path.
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

        // An event branch fired from the CRM side (trigger_telephony_event) has no ESL connection.
        // campaign_queue opens its own (as tf_secure_collect does); the other destinations still need one.
        await using var ownedEsl = ctx.Esl is null && destType == "campaign_queue"
            ? await (_services.GetService<IEslCommanderFactory>()?.CreateAsync(ct) ?? Task.FromResult<IOwnedEslCommander>(null!))
            : null;
        var esl = ctx.Esl ?? ownedEsl;
        if (esl is null)
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
            _                 => await TransferToCampaignQueueAsync(node, ctx, transitions, esl, ct),
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
                gw = _config["FreeSWITCH:DefaultGateway"] ?? "signalwire";
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
        JsonObject node, TelephonyFlowContext ctx, JsonObject? transitions, IEslCommander esl, CancellationToken ct)
    {
        // An agent already on the call → mid-call cold transfer (S178).
        var agentLeg = ctx.Vars.GetValueOrDefault("_bridged_peer_uuid") is { Length: > 0 } peer
            ? peer : ctx.Vars.GetValueOrDefault("_agent_uuid");
        var fromAgentId = ctx.Vars.GetValueOrDefault("_assigned_agent_id");
        var midCall = !string.IsNullOrEmpty(agentLeg) && !string.IsNullOrEmpty(fromAgentId);

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

        // Mid-call: the agent tells the caller they're being transferred; the deferred tts_play announcement
        // would itself unbridge the call, so it's skipped and the caller hears hold music instead.
        if (!midCall && await PlayAnnouncementAsync(node, ctx, ct))
            return new TelephonyNodeResult(null, "transferring");

        // The answering agent gets the target campaign's script. Without this, tf_script_pop would pick the
        // dialed number's script first, i.e. the sales script on a call transferred out of sales.
        if (!ctx.Vars.ContainsKey("_screenpop_flow_override") && target.FlowId is { } targetFlow)
            ctx.Vars["_screenpop_flow_override"] = targetFlow.ToString();

        if (midCall && !await PullCallerFromAgentAsync(ctx, esl, agentLeg!, fromAgentId!, ct))
            return Follow(transitions, "failed");

        var ranked = await _ranker.GetRankedEligibleAgentsAsync(db, ctx.TenantId, targetCampaignId, ct: ct);
        var eligible = target.RingStrategy == CampaignRingStrategy.RingTopNByProficiency
            ? ranked.Take(target.RingTopN)
            : ranked;
        var eligibleIds = eligible.Select(r => r.AgentId).ToList();

        // Before any agent has the call (e.g. an IVR routing to CS), the record moves onto the target campaign:
        // nobody else worked it. Mid-call it stays put, so the sale, commissions and media attribution remain
        // with the original campaign; routing follows the session's campaign. Either way the engine updates
        // the Redis session's CampaignId (a handler can't persist that itself — see
        // TelephonyFlowEngine.ApplyPendingSessionMutations).
        if (!midCall)
        {
            var record = await db.CallRecords.FirstOrDefaultAsync(r => r.Id == ctx.CallRecordId, ct);
            record?.SetCampaign(targetCampaignId, target.ClientId);
            if (record is not null) await db.SaveChangesAsync(ct);
        }

        ctx.Vars["_switch_campaign_id"] = targetCampaignId.ToString();
        ctx.Vars["_queued"]            = "true";
        ctx.Vars["_eligible_agents"]   = string.Join(",", eligibleIds);
        ctx.Vars["_in_queue_at"]       = DateTimeOffset.UtcNow.ToString("O");
        // A prior queue's timeout target no longer applies to the new campaign.
        ctx.Vars.Remove("_on_timeout_node_id");
        // Nor do its parallel-queuing offer restrictions (e.g. Elite-only), which would make delivery refuse
        // every agent in the new campaign.
        ctx.RemoveSessionVar(QueueOffer.RestrictGroupVar);
        ctx.RemoveSessionVar("_eligible_tier_labels");

        await _callStateRecorder.RecordAsync(
            ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId,
            CallHistoryState.InQueue, targetCampaignId, agentId: null, detail: "transferred", ct: ct);

        _logger.LogInformation(
            "TransferNodeHandler [{Uuid}]: transferred to campaign {Campaign} queue ({Count} eligible agent(s)){MidCall}",
            ctx.ChannelUuid, targetCampaignId, eligibleIds.Count, midCall ? " — mid-call, from agent " + fromAgentId : "");

        return Follow(transitions, "transferred");
    }

    /// <summary>
    /// Mid-call cold transfer: caller → hold music, agent's leg dropped. The guards are saved to the live
    /// session BEFORE touching the bridge, because CHANNEL_UNBRIDGE / CHANNEL_HANGUP arrive while this branch
    /// is still running and would otherwise read the teardown as the end of the call (Take Over pattern, S167):
    ///   _requeue_in_progress    — CHANNEL_UNBRIDGE leaves the caller up; cleared on the next bridge.
    ///   _requeue_old_leg        — that leg's CHANNEL_HANGUP isn't the end of the call; the agent goes to ACW.
    ///   _requeued_from_agent_id — who to put in ACW.
    ///   _keep_record_agent      — delivery leaves the record's agent / routing tier alone (sales attribution).
    /// </summary>
    private async Task<bool> PullCallerFromAgentAsync(
        TelephonyFlowContext ctx, IEslCommander esl, string agentLeg, string fromAgentId, CancellationToken ct)
    {
        var guards = new Dictionary<string, string>
        {
            ["_requeue_in_progress"]    = "true",
            ["_requeue_old_leg"]        = agentLeg,
            ["_requeued_from_agent_id"] = fromAgentId,
            ["_keep_record_agent"]      = "true",
        };
        foreach (var (k, v) in guards) ctx.Vars[k] = v;

        var live = await _sessionStore.GetAsync(ctx.ChannelUuid, ct);
        if (live is null)
        {
            _logger.LogWarning("TransferNodeHandler [{Uuid}]: mid-call transfer — no live session", ctx.ChannelUuid);
            return false;
        }
        foreach (var (k, v) in guards) live.Vars[k] = v;
        await _sessionStore.SaveAsync(live, ct);

        try
        {
            // The dropped leg must end, not re-park, once unbridged; the caller (park_after_bridge=true since
            // tf_answer) goes to hold music and stays parked for the next agent.
            await esl.SetChannelVarAsync(agentLeg, "park_after_bridge", "false", ct);
            await esl.TransferAsync(ctx.ChannelUuid, "park_with_moh", "XML", "default", ct);
            await esl.HangupChannelAsync(agentLeg, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TransferNodeHandler [{Uuid}]: mid-call transfer — leg pull failed", ctx.ChannelUuid);
            return false;
        }

        // The old agent is off the call. _assigned_agent_id is re-set by delivery for the next agent; leaving
        // it would make a caller hang-up while queued restore the sales agent's state.
        foreach (var k in new[] { "_assigned_agent_id", "_bridged_peer_uuid", "_agent_uuid" })
            ctx.RemoveSessionVar(k);

        _logger.LogInformation(
            "TransferNodeHandler [{Uuid}]: mid-call transfer — caller on hold, agent {Agent} leg {Leg} dropped",
            ctx.ChannelUuid, fromAgentId, agentLeg);
        return true;
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
