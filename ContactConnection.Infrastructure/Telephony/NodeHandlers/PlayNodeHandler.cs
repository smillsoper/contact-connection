using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Plays audio on the live call channel via FreeSWITCH uuid_broadcast.
///
/// Audio sources:
///   file     — tenant-uploaded file or built-in FreeSWITCH path (prefix "__builtin:")
///              Special: "local_stream://moh" and "silence_stream://..." passed through as-is.
///   tts      — FreeSWITCH say/flite: requires freeswitch-mod-flite in the container.
///
/// The node fires the media and returns immediately (fire-and-forget).
/// Loop/continuation state is stored in ctx.Vars and handled by PLAYBACK_STOP
/// in EslBackgroundService, which calls TelephonyFlowEngine.ResumeFromNodeAsync.
/// </summary>
public class PlayNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_play";

    private readonly ITenantDbContextFactory _factory;
    private readonly IConfiguration _config;
    private readonly ILogger<PlayNodeHandler> _logger;

    public PlayNodeHandler(
        ITenantDbContextFactory factory,
        IConfiguration config,
        ILogger<PlayNodeHandler> logger)
    {
        _factory = factory;
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
                return TerminalResult(transitions, "_play_next_tts_finished");
            }
            // FreeSWITCH flite syntax — requires mod_flite in the container
            var encodedText = ttsText.Replace(" ", "%20").Replace("\n", " ");
            mainMediaArg = $"say:en+flite+{ttsVoice}+{encodedText}";
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

        // Store next-node IDs for each possible exit transition
        if (transitions is not null)
        {
            foreach (var (key, val) in transitions)
            {
                var nodeId = val?.GetValue<string>();
                if (!string.IsNullOrEmpty(nodeId))
                    ctx.Vars[$"_play_next_{key}"] = nodeId;
            }
        }

        // ── Fire the broadcast ───────────────────────────────────────────────────
        _logger.LogInformation(
            "PlayNodeHandler [{Uuid}]: broadcasting '{MediaArg}' loop={Loop} duration={Duration}s",
            ctx.ChannelUuid, mainMediaArg, autoRestart, durationSeconds);

        await ctx.Esl.BroadcastAsync(ctx.ChannelUuid, mainMediaArg, ct);

        // Terminal — EslBackgroundService picks up from PLAYBACK_STOP
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

    private static TelephonyNodeResult TerminalResult(JsonObject? transitions, string preferredKey)
    {
        var nextNodeId = transitions?[preferredKey]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, preferredKey);
    }
}
