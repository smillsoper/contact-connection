using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Plays a pre-bridge announcement on the agent's leg only — an audio file, or (audioSource
/// = "tts") free text spoken by flite or, when the tenant has a TTS-streaming vendor configured,
/// that vendor's voice via a foreground shout:// MP3 in the tts_play dialplan extension.
///
/// Requires that AnswerQueuedCall stored "_agent_uuid" in the caller session before
/// firing the agent_selected event branch. The caller and agent are NOT bridged until
/// this branch reaches tf_end (which calls BridgeChannelsAsync).
///
/// Resume (both are keyed on the agent channel UUID → whisper:{agentUuid} → callerUuid →
/// "_whisper_next_default"):
///   file / flite  — uuid_broadcast on the agent leg → PLAYBACK_STOP → HandleWhisperPlaybackStopAsync
///   streaming     — uuid_transfer the agent leg into tts_play → contactconnection::tts_done →
///                   HandleTtsDoneAsync (whisper branch) → ResumeWhisperAsync
/// </summary>
public class WhisperNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_whisper";

    private readonly ITenantDbContextFactory _factory;
    private readonly ITtsStreamingService _tts;
    private readonly IConfiguration _config;
    private readonly ILogger<WhisperNodeHandler> _logger;

    public WhisperNodeHandler(
        ITenantDbContextFactory factory,
        ITtsStreamingService tts,
        IConfiguration config,
        ILogger<WhisperNodeHandler> logger)
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
            _logger.LogWarning("WhisperNodeHandler [{Uuid}]: no ESL connection available", ctx.ChannelUuid);
            return new TelephonyNodeResult(null, "error");
        }

        if (!ctx.Vars.TryGetValue("_agent_uuid", out var agentUuid) || string.IsNullOrEmpty(agentUuid))
        {
            _logger.LogWarning("WhisperNodeHandler [{Uuid}]: _agent_uuid not set — cannot whisper", ctx.ChannelUuid);
            return new TelephonyNodeResult(null, "error");
        }

        // Store the continuation node ID so PLAYBACK_STOP can resume from it
        var transitions = node["transitions"]?.AsObject();
        var nextNodeId  = transitions?["default"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(nextNodeId))
            ctx.Vars["_whisper_next_default"] = nextNodeId;

        var audioSource = node["audioSource"]?.GetValue<string>() ?? "file";
        string? mediaArg;

        if (audioSource == "tts")
        {
            var ttsText  = node["ttsText"]?.GetValue<string>() ?? "";
            var ttsVoice = node["ttsVoice"]?.GetValue<string>() ?? "kal";
            if (string.IsNullOrWhiteSpace(ttsText))
            {
                _logger.LogWarning(
                    "WhisperNodeHandler [{Uuid}]: TTS text is empty — skipping whisper, resuming flow",
                    ctx.ChannelUuid);
                return new TelephonyNodeResult(nextNodeId, "default");
            }

            var provider = await _tts.ResolveProviderAsync(ctx.TenantSchemaName, ct);
            if (provider is not null)
            {
                // Streaming vendor voice — one continuous shout:// MP3. Same reason as tf_play: it
                // can't be uuid_broadcast (mod_shout never fires PLAYBACK_STOP on a finite stream),
                // so uuid_transfer the AGENT leg into the tts_play extension for a foreground
                // playback. tts_done → EslBackgroundService.HandleTtsDoneAsync sees whisper:{agentUuid}
                // and runs the same whisper→bridge resume the flite PLAYBACK_STOP path takes.
                var streamUrl = await _tts.PrepareStreamUrlAsync(ctx.TenantSubdomain, provider, ttsText, ttsVoice, ct);
                _logger.LogInformation(
                    "WhisperNodeHandler [{Uuid}]: streaming TTS via {Url} → tts_play on agent channel {AgentUuid}",
                    ctx.ChannelUuid, streamUrl, agentUuid);

                // Brief settle window for WebRTC ICE/DTLS after the SIP 200 OK (originate returns
                // +OK before media flows). 600ms is ample for a local-network handshake.
                await Task.Delay(600, ct);
                await ctx.Esl.SetChannelVarAsync(agentUuid, "cc_tts_url", streamUrl, ct);
                await ctx.Esl.TransferAsync(agentUuid, "tts_play", "XML", "default", ct);
                return new TelephonyNodeResult(null, "whisper_playing");
            }
            else
            {
                // flite "tts" file-string on the AGENT channel. Same channel-var indirection as
                // PlayNodeHandler: uuid_broadcast's arg parser is "<uuid> <path> [leg]", so a
                // <path> containing raw spaces folds the leg flag into the path — routing the text
                // through ${cc_tts_text} keeps the command line space-free. FreeSWITCH does not
                // URL-decode the text segment, so literal spaces are required.
                var sanitizedText = ttsText.Replace("\n", " ");
                await ctx.Esl.SetChannelVarAsync(agentUuid, "cc_tts_text", sanitizedText, ct);
                mediaArg = $"tts://flite|{ttsVoice}|${{cc_tts_text}}";
                _logger.LogInformation(
                    "WhisperNodeHandler [{Uuid}]: TTS via flite voice={Voice} on agent channel {AgentUuid}",
                    ctx.ChannelUuid, ttsVoice, agentUuid);
            }
        }
        else
        {
            var audioFileId = node["audioFileId"]?.GetValue<string>() ?? "";
            mediaArg = await ResolveFileArgAsync(audioFileId, ctx, ct);
            if (mediaArg is null)
            {
                _logger.LogWarning("WhisperNodeHandler [{Uuid}]: could not resolve audio file '{Id}'", ctx.ChannelUuid, audioFileId);
                return new TelephonyNodeResult(null, "error");
            }
        }

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

    // Shared with every other telephony node handler — see TelephonyAudioResolver.
    private Task<string?> ResolveFileArgAsync(
        string audioFileId, TelephonyFlowContext ctx, CancellationToken ct) =>
        TelephonyAudioResolver.ResolveFileArgAsync(_factory, _config, audioFileId, ctx.TenantSchemaName, ct);
}
