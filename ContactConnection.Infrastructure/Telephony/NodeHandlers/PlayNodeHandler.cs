using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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
///          - TtsStreaming preference configured: routed through mod_audio_stream + the Api's
///            /relay/tts-stream WebSocket relay to an external vendor (Azure, ElevenLabs, ...)
///            via ITtsStreamProvider, fired with uuid_audio_stream. See ITtsStreamingService.
///
/// The node fires the media and returns immediately (fire-and-forget). Continuation is handled
/// by EslBackgroundService: PLAYBACK_STOP for the uuid_broadcast paths (file, flite tts), or
/// mod_audio_stream::disconnect for the streaming tts path — both ultimately call
/// TelephonyFlowEngine.ResumeFromNodeAsync via the same "_play_next_{transition}" session vars.
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

        // ── Fire the broadcast ───────────────────────────────────────────────────
        _logger.LogInformation(
            "PlayNodeHandler [{Uuid}]: broadcasting '{MediaArg}' loop={Loop} duration={Duration}s",
            ctx.ChannelUuid, mainMediaArg, autoRestart, durationSeconds);

        await ctx.Esl.BroadcastAsync(ctx.ChannelUuid, mainMediaArg, ct);

        // Terminal — EslBackgroundService picks up from PLAYBACK_STOP
        return new TelephonyNodeResult(null, "playing");
    }

    /// <summary>
    /// Arms the live mod_audio_stream session via the shared ITtsStreamingService, then lays down
    /// this node's own continuation bookkeeping — same session-var convention as the
    /// uuid_broadcast path so EslBackgroundService's existing FireEndTransitionAsync
    /// ("tts_finished" transition) works unchanged, only the triggering FreeSWITCH event differs
    /// (mod_audio_stream::disconnect, not PLAYBACK_STOP). No "_play_media_arg" — nothing to
    /// loop/re-broadcast on this path.
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

        ctx.Vars["_play_state"]        = "streaming";
        ctx.Vars["_play_audio_source"] = "tts";
        ctx.Vars["_play_started_at"]   = DateTimeOffset.UtcNow.ToString("O");
        StoreTransitions(transitions, ctx.Vars);

        await _tts.StartStreamAsync(ctx, provider, ttsText, ttsVoice, ct);

        return new TelephonyNodeResult(null, "playing");
    }

    private async Task<string?> ResolveFileArgAsync(
        string audioFileId, TelephonyFlowContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(audioFileId))
            return null;

        // Pass-through special stream identifiers (local_stream, silence_stream, tone_stream)
        if (audioFileId.StartsWith("local_stream://") ||
            audioFileId.StartsWith("silence_stream://") ||
            audioFileId.StartsWith("tone_stream://"))
            return audioFileId;

        // Built-in FreeSWITCH path (skips DB lookup)
        if (audioFileId.StartsWith("__builtin:"))
            return audioFileId["__builtin:".Length..];

        // Tenant-uploaded file — look up stored filename
        if (!Guid.TryParse(audioFileId, out var fileId))
            return null;

        await using var db = _factory.Create(ctx.TenantSchemaName);
        var audioFile = await db.AudioFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (audioFile is null)
            return null;

        var containerBase = _config["FreeSWITCH:SoundsContainerPath"]
            ?? "/usr/share/freeswitch/sounds/contactconnection";

        return $"{containerBase}/{ctx.TenantSchemaName}/{audioFile.StoredFileName}";
    }

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
