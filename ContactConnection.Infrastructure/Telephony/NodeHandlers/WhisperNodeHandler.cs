using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Plays an audio file on the agent's leg only (pre-bridge whisper announcement).
///
/// Requires that AnswerQueuedCall stored "_agent_uuid" in the caller session before
/// firing the agent_selected event branch. The caller and agent are NOT bridged until
/// this branch reaches tf_end (which calls BridgeChannelsAsync).
///
/// PLAYBACK_STOP on the agent's channel UUID → EslBackgroundService looks up
/// whisper:{agentUuid} → callerUuid → resumes from "_whisper_next_default" node.
/// </summary>
public class WhisperNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_whisper";

    private readonly ITenantDbContextFactory _factory;
    private readonly IConfiguration _config;
    private readonly ILogger<WhisperNodeHandler> _logger;

    public WhisperNodeHandler(
        ITenantDbContextFactory factory,
        IConfiguration config,
        ILogger<WhisperNodeHandler> logger)
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
            _logger.LogWarning("WhisperNodeHandler [{Uuid}]: no ESL connection available", ctx.ChannelUuid);
            return new TelephonyNodeResult(null, "error");
        }

        if (!ctx.Vars.TryGetValue("_agent_uuid", out var agentUuid) || string.IsNullOrEmpty(agentUuid))
        {
            _logger.LogWarning("WhisperNodeHandler [{Uuid}]: _agent_uuid not set — cannot whisper", ctx.ChannelUuid);
            return new TelephonyNodeResult(null, "error");
        }

        var audioFileId = node["audioFileId"]?.GetValue<string>() ?? "";
        var mediaArg    = await ResolveFileArgAsync(audioFileId, ctx, ct);
        if (mediaArg is null)
        {
            _logger.LogWarning("WhisperNodeHandler [{Uuid}]: could not resolve audio file '{Id}'", ctx.ChannelUuid, audioFileId);
            return new TelephonyNodeResult(null, "error");
        }

        // Store the continuation node ID so PLAYBACK_STOP can resume from it
        var transitions = node["transitions"]?.AsObject();
        var nextNodeId  = transitions?["default"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(nextNodeId))
            ctx.Vars["_whisper_next_default"] = nextNodeId;

        ctx.Vars["_whisper_media_arg"] = mediaArg;

        _logger.LogInformation(
            "WhisperNodeHandler [{Uuid}]: broadcasting '{MediaArg}' on agent channel {AgentUuid}",
            ctx.ChannelUuid, mediaArg, agentUuid);

        // Allow a brief window for WebRTC ICE/DTLS to finish negotiating after the SIP 200 OK.
        // originate returns +OK as soon as FreeSWITCH receives the 200 OK — media may not be
        // flowing yet. 600ms is ample for a local-network DTLS handshake.
        await Task.Delay(600, ct);

        // Broadcast on the agent's channel (not the caller's)
        await ctx.Esl.BroadcastAsync(agentUuid, mediaArg, ct);

        return new TelephonyNodeResult(null, "whisper_playing");
    }

    private async Task<string?> ResolveFileArgAsync(
        string audioFileId, TelephonyFlowContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(audioFileId)) return null;

        if (audioFileId.StartsWith("local_stream://") ||
            audioFileId.StartsWith("silence_stream://") ||
            audioFileId.StartsWith("tone_stream://"))
            return audioFileId;

        if (audioFileId.StartsWith("__builtin:"))
            return audioFileId["__builtin:".Length..];

        if (!Guid.TryParse(audioFileId, out var fileId)) return null;

        await using var db  = _factory.Create(ctx.TenantSchemaName);
        var audioFile       = await db.AudioFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (audioFile is null) return null;

        var containerBase = _config["FreeSWITCH:SoundsContainerPath"]
            ?? "/usr/share/freeswitch/sounds/contactconnection";

        return $"{containerBase}/{ctx.TenantSchemaName}/{audioFile.StoredFileName}";
    }
}
