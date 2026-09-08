using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Plays audio on the live call channel.
///
/// Audio sources:
///   file — tenant-uploaded file or built-in FreeSWITCH path (prefix "__builtin:"), via
///          uuid_broadcast. Special: "local_stream://moh" and "silence_stream://..." passed
///          through as-is.
///   tts  — one of two paths, chosen per-tenant:
///          - No TtsStreaming preference (default): FreeSWITCH tts:// file string via flite,
///            fired with uuid_broadcast. Requires freeswitch-mod-flite in the container.
///          - TtsStreaming preference configured: one continuous shout:// MP3 from the Api's
///            /relay/tts-mp3 endpoint (external vendor via ITtsStreamProvider), played *foreground*
///            in the tts_play dialplan extension via uuid_transfer. See StartStreamingTtsAsync.
///
/// The node fires the media and returns immediately (fire-and-forget). Continuation is handled
/// by EslBackgroundService: PLAYBACK_STOP for the uuid_broadcast paths (file, flite tts), or the
/// contactconnection::tts_done CUSTOM event for the streaming tts path — both ultimately call
/// TelephonyFlowEngine.ResumeFromNodeAsync on the node's "tts_finished" / "end_of_stream" transition.
/// </summary>
public class PlayNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_play";

    private readonly ITenantDbContextFactory _factory;
    private readonly ITtsStreamingService _tts;
    private readonly IConfiguration _config;
    private readonly ILogger<PlayNodeHandler> _logger;

    public PlayNodeHandler(
        ITenantDbContextFactory factory,
        ITtsStreamingService tts,
        IConfiguration config,
        ILogger<PlayNodeHandler> logger)
    {
        _factory = factory;
        _tts     = tts;
        _config  = config;
        _logger  = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        if (ctx.Esl is null)
        {
            _logger.LogWarning("PlayNodeHandler [{Uuid}]: no ESL connection available", ctx.ChannelUuid);
            return new TelephonyNodeResult(null, "error");
        }

        var audioSource = node["audioSource"]?.GetValue<string>() ?? "file";
        var autoRestart = node["autoRestart"]?.GetValue<bool>() ?? false;
        var durationSeconds = node["durationSeconds"]?.GetValue<int>() ?? 0;
        var startOffsetSeconds = node["startOffsetSeconds"]?.GetValue<int>() ?? 0;
        var periodicAnnouncementInterval = node["periodicAnnouncementIntervalSeconds"]?.GetValue<int>() ?? 30;

        var transitions = node["transitions"]?.AsObject();

        // ── Resolve main media arg ───────────────────────────────────────────────
        string? mainMediaArg;

        if (audioSource == "tts")
        {
            var ttsText  = node["ttsText"]?.GetValue<string>() ?? "";
            var ttsVoice = node["ttsVoice"]?.GetValue<string>() ?? "kal";
            if (string.IsNullOrWhiteSpace(ttsText))
            {
                _logger.LogWarning("PlayNodeHandler [{Uuid}]: TTS text is empty — skipping", ctx.ChannelUuid);
                // Bug fix: this was "_play_next_tts_finished" (the session-var name, not the
                // transitions-object key) — TerminalResult looks up transitions[key] directly,
                // so it never matched anything and silently dead-ended the flow whenever a tts
                // node had empty text.
                return TerminalResult(transitions, "tts_finished");
            }

            var streamingProvider = await _tts.ResolveProviderAsync(ctx.TenantSchemaName, ct);
            if (streamingProvider is not null)
                return await StartStreamingTtsAsync(ctx, streamingProvider, ttsText, ttsVoice, transitions, autoRestart, ct);

            // FreeSWITCH TTS file-string syntax (mod_dptools' "tts" file format, backed by
            // mod_flite) — "say:" is a different subsystem (phrase/number macros) and throws
            // "Invalid Args" if used for free text. The text segment is NOT URL-decoded by the
            // parser, so literal spaces are required — percent-encoding them gets read aloud
            // ("%20" -> "percent twenty") instead of treated as whitespace.
            //
            // uuid_broadcast's own arg parser is "<uuid> <path> [aleg|bleg|holdb|both]" — when
            // <path> itself contains raw spaces, the trailing leg flag we append gets folded
            // into the path instead of being recognized as the leg selector, so FreeSWITCH
            // speaks the literal word "aleg". Routing the text through a channel variable keeps
            // the broadcast command line itself space-free (leg parsing stays unambiguous);
            // FreeSWITCH expands ${cc_tts_text} back to the full text before flite renders it.
            var sanitizedText = ttsText.Replace("\n", " ");
            await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_tts_text", sanitizedText, ct);
            mainMediaArg = $"tts://flite|{ttsVoice}|${{cc_tts_text}}";
            _logger.LogInformation("PlayNodeHandler [{Uuid}]: TTS via flite voice={Voice}", ctx.ChannelUuid, ttsVoice);
        }
        else
        {
            var audioFileId = node["audioFileId"]?.GetValue<string>() ?? "";
            mainMediaArg = await ResolveFileArgAsync(audioFileId, ctx, ct);
            if (mainMediaArg is null)
            {
                _logger.LogWarning("PlayNodeHandler [{Uuid}]: could not resolve audio file '{Id}'", ctx.ChannelUuid, audioFileId);
                return new TelephonyNodeResult(null, "error");
            }
        }

        // Apply start offset if set
        if (startOffsetSeconds > 0)
            mainMediaArg = $"{mainMediaArg}@@{startOffsetSeconds * 1000}";

        // ── Resolve periodic announcement playlist ───────────────────────────────
        var resolvedAnnouncements = new List<string>();
        if (node["periodicAnnouncements"] is JsonArray announcementsArr)
        {
            foreach (var item in announcementsArr)
            {
                var fileId = item?["fileId"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(fileId)) continue;
                var arg = await ResolveFileArgAsync(fileId, ctx, ct);
                if (arg is not null) resolvedAnnouncements.Add(arg);
            }
        }

        // ── Store play state in Vars (read by EslBackgroundService on PLAYBACK_STOP) ──
        ctx.Vars["_play_media_arg"]    = mainMediaArg;
        ctx.Vars["_play_loop"]         = autoRestart ? "true" : "false";
        ctx.Vars["_play_audio_source"] = audioSource;
        ctx.Vars["_play_state"]        = "main";
        ctx.Vars["_play_started_at"]   = DateTimeOffset.UtcNow.ToString("O");

        if (durationSeconds > 0)
            ctx.Vars["_play_duration_seconds"] = durationSeconds.ToString();

        if (resolvedAnnouncements.Count > 0)
        {
            ctx.Vars["_play_announcements_json"]    = JsonSerializer.Serialize(resolvedAnnouncements);
            ctx.Vars["_play_announcement_index"]    = "0";
            ctx.Vars["_play_announcement_interval"] = periodicAnnouncementInterval.ToString();
            ctx.Vars["_play_last_announcement_at"]  = "";
        }

        StoreTransitions(transitions, ctx.Vars);

        // Optional per-node lead-in silence — plays a short silence burst and waits for it to
        // finish before the real prompt, priming the RTP path so the first syllable isn't
        // clipped. tf_answer already does this once right after answer; this is for a prompt
        // that follows a long silent gap (e.g. a slow API call) where the path may have gone
        // cold again. Default 0 (rely on the answer-time prime). File / flite-TTS paths only —
        // the streaming-TTS path returned earlier.
        var leadInMs = node["leadInSilenceMs"]?.GetValue<int>() ?? 0;
        if (leadInMs > 0)
        {
            await ctx.Esl.BroadcastAsync(ctx.ChannelUuid, $"silence_stream://{leadInMs},0", ct);
            await Task.Delay(leadInMs + 80, ct);
        }

        // ── Fire the broadcast ───────────────────────────────────────────────────
        _logger.LogInformation(
            "PlayNodeHandler [{Uuid}]: broadcasting '{MediaArg}' loop={Loop} duration={Duration}s",
            ctx.ChannelUuid, mainMediaArg, autoRestart, durationSeconds);

        await ctx.Esl.BroadcastAsync(ctx.ChannelUuid, mainMediaArg, ct);

        // Terminal — EslBackgroundService picks up from PLAYBACK_STOP
        return new TelephonyNodeResult(null, "playing");
    }

    /// <summary>
    /// Streaming-vendor TTS: one continuous shout:// MP3. It can't be <c>uuid_broadcast</c>ed like a
    /// file — mod_shout treats the HTTP stream as an infinite Icecast source and never fires
    /// PLAYBACK_STOP on body EOF, so the flow would hang here forever. Instead <c>uuid_transfer</c>
    /// the caller into the <c>tts_play</c> dialplan extension, whose *foreground* playback does see
    /// EOF; it then emits <c>contactconnection::tts_done</c> and re-parks. EslBackgroundService's
    /// HandleTtsDoneAsync resolves <c>_tts_next_finished</c> and resumes the flow — the same
    /// "tts_finished" transition the flite path takes, just off a CUSTOM event instead of
    /// PLAYBACK_STOP. No "_play_*" bookkeeping (nothing to loop/re-broadcast).
    /// </summary>
    private async Task<TelephonyNodeResult> StartStreamingTtsAsync(
        TelephonyFlowContext ctx,
        TtsStreamingProviderInfo provider,
        string ttsText,
        string ttsVoice,
        JsonObject? transitions,
        bool autoRestart,
        CancellationToken ct)
    {
        if (autoRestart)
            _logger.LogWarning(
                "PlayNodeHandler [{Uuid}]: autoRestart is not supported for streaming TTS — ignoring",
                ctx.ChannelUuid);

        var streamUrl = await _tts.PrepareStreamUrlAsync(ctx.TenantSubdomain, provider, ttsText, ttsVoice, ct);

        // Resume on the TTS-Finished handle, falling back to the generic playback-done / default
        // handle — a node switched from file to TTS in the designer often still has its edge on
        // end_of_stream / default rather than the tts_finished handle. Mirrors FireEndTransitionAsync.
        ctx.Vars["_tts_in_progress"]  = "true";
        ctx.Vars["_tts_next_finished"] =
            transitions?["tts_finished"]?.GetValue<string>()
            ?? transitions?["end_of_stream"]?.GetValue<string>()
            ?? transitions?["default"]?.GetValue<string>()
            ?? string.Empty;

        _logger.LogInformation("PlayNodeHandler [{Uuid}]: streaming TTS via {Url} → tts_play", ctx.ChannelUuid, streamUrl);
        await ctx.Esl!.SetChannelVarAsync(ctx.ChannelUuid, "cc_tts_url", streamUrl, ct);
        await ctx.Esl!.TransferAsync(ctx.ChannelUuid, "tts_play", "XML", "default", ct);

        // Terminal — EslBackgroundService resumes from the contactconnection::tts_done event.
        return new TelephonyNodeResult(null, "playing");
    }

    // Shared with every other telephony node handler — see TelephonyAudioResolver.
    private Task<string?> ResolveFileArgAsync(
        string audioFileId, TelephonyFlowContext ctx, CancellationToken ct) =>
        TelephonyAudioResolver.ResolveFileArgAsync(_factory, _config, audioFileId, ctx.TenantSchemaName, ct);

    private static void StoreTransitions(JsonObject? transitions, Dictionary<string, string> vars)
    {
        if (transitions is null) return;
        foreach (var (key, val) in transitions)
        {
            var nodeId = val?.GetValue<string>();
            if (!string.IsNullOrEmpty(nodeId))
                vars[$"_play_next_{key}"] = nodeId;
        }
    }

    private static TelephonyNodeResult TerminalResult(JsonObject? transitions, string preferredKey)
    {
        var nextNodeId = transitions?[preferredKey]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, preferredKey);
    }
}
