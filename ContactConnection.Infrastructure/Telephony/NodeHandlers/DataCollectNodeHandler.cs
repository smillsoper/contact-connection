using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_data_collect — plays a prompt, collects a value (DTMF, and optionally spoken voice), and
/// writes it verbatim into a named flow variable. No per-value branching: unlike tf_ivr_menu
/// there are no "options" — any digits (or, if voice is enabled, any transcript) are accepted and
/// stored as-is. Two exits only: "collected" (non-empty value captured) and "timeout" (nothing
/// captured before retries/the capture window ran out).
///
/// DTMF reuses tf_ivr_menu's exact mechanism — cc_dc_* channel vars + `uuid_transfer` into the
/// "data_collect" dialplan extension (its own extension/event names so it can't be confused with
/// an in-flight tf_ivr_menu capture on the same channel; this build has no uuid_execute) — but
/// with an "any digits" regexp (IvrMenu.BuildRegexp with no option keys) since there's nothing to
/// match against. EslBackgroundService's contactconnection::data_collect_done handler picks up
/// the result.
///
/// allowVoice (optional) starts a concurrent free-form STT capture (mod_audio_stream tapping the
/// caller's mic — passive, doesn't touch playback) alongside the DTMF collection, using the same
/// tenant SttStreaming vendor preference tf_ivr_menu's voice option resolves. Unlike tf_ivr_menu,
/// there's no phrase matching: the first non-empty FINAL transcript is taken verbatim as the
/// captured value. Whichever of (DTMF collection completes, a transcript is captured) happens
/// first wins via IDataCollectResolutionCoordinator — same "exclusive Redis claim" arbitration
/// IIvrVoiceResolutionCoordinator uses for tf_ivr_menu's voice race. The loser's completion event
/// (if play_and_get_digits is still running when voice wins) lands after `_dc_in_progress` has
/// already been cleared by the winner and is a no-op — same idempotent-stale-event guard pattern
/// tf_ivr_menu/tf_secure_collect/tf_delay all use, so there's no need to forcibly abort
/// play_and_get_digits early.
/// </summary>
public class DataCollectNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_data_collect";

    private readonly ITenantDbContextFactory _factory;
    private readonly ISttStreamingService _sttStreaming;
    private readonly IConfiguration _config;
    private readonly ILogger<DataCollectNodeHandler> _logger;

    public DataCollectNodeHandler(
        ITenantDbContextFactory factory, ISttStreamingService sttStreaming, IConfiguration config,
        ILogger<DataCollectNodeHandler> logger)
    {
        _factory      = factory;
        _sttStreaming = sttStreaming;
        _config       = config;
        _logger       = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var transitions  = node["transitions"]?.AsObject();
        var nodeId       = node["nodeId"]?.GetValue<string>() ?? "tf_data_collect";
        var variableName = node["variableName"]?.GetValue<string>()?.Trim();

        if (string.IsNullOrEmpty(variableName))
        {
            _logger.LogWarning(
                "DataCollectNodeHandler [{Uuid}]: no variableName configured — nothing to capture into", ctx.ChannelUuid);
            return Follow(transitions, "timeout");
        }

        if (ctx.Esl is null)
        {
            _logger.LogWarning("DataCollectNodeHandler [{Uuid}]: no ESL connection — cannot collect", ctx.ChannelUuid);
            return Follow(transitions, "timeout");
        }

        var minDigits           = node["minDigits"]?.GetValue<int>() ?? 1;
        var maxDigits           = node["maxDigits"]?.GetValue<int>() ?? 20;
        var maxTries            = node["maxTries"]?.GetValue<int>() ?? 3;
        var timeoutMs           = node["timeoutMs"]?.GetValue<int>() ?? 5000;
        var interDigitTimeoutMs = node["interDigitTimeoutMs"]?.GetValue<int>() ?? 3000;
        var terminators         = node["terminators"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(terminators))
            terminators = maxDigits > 1 ? "#" : "none";

        var promptArg = await TelephonyAudioResolver.ResolveFileArgAsync(
            _factory, _config, node["promptAudioFileId"]?.GetValue<string>(), ctx.TenantSchemaName, ct);

        if (promptArg is null)
        {
            _logger.LogWarning("DataCollectNodeHandler [{Uuid}]: no prompt audio configured", ctx.ChannelUuid);
            return Follow(transitions, "timeout");
        }

        var invalidArg = await TelephonyAudioResolver.ResolveFileArgAsync(
            _factory, _config, node["invalidAudioFileId"]?.GetValue<string>(), ctx.TenantSchemaName, ct)
            ?? "silence_stream://250";

        var collectedTarget = transitions?["collected"]?.GetValue<string>() ?? string.Empty;
        var timeoutTarget    = transitions?["timeout"]?.GetValue<string>() ?? string.Empty;

        // "Any digits" — no options to build a restrictive alternation from.
        var regexp = IvrMenu.BuildRegexp(Array.Empty<string>());

        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_dc_min", minDigits.ToString(), ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_dc_max", maxDigits.ToString(), ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_dc_tries", maxTries.ToString(), ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_dc_timeout", timeoutMs.ToString(), ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_dc_term", terminators, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_dc_prompt", promptArg, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_dc_invalid", invalidArg, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_dc_regex", regexp, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_dc_digit_timeout", interDigitTimeoutMs.ToString(), ct);

        ctx.Vars["_dc_in_progress"]   = "true";
        ctx.Vars["_dc_node_id"]       = nodeId;
        ctx.Vars["_dc_variable_name"] = variableName;
        ctx.Vars["_dc_next_node"]     = collectedTarget;
        ctx.Vars["_dc_timeout_node"]  = timeoutTarget;
        // Read by DataCollectResolutionCoordinator when it writes the resolved value — strips
        // punctuation a vendor's transcript may add (live-observed: a trailing "." on an otherwise
        // clean 10-digit phone number) and maps spelled-out digit words to numerals, in case a
        // vendor ever spells digits out instead of using numerals. DTMF digits are already clean,
        // so this is a no-op for the DTMF side either way.
        ctx.Vars["_dc_numeric_only"]  = (node["numericOnly"]?.GetValue<bool>() ?? false) ? "true" : "false";

        var allowVoice = node["allowVoice"]?.GetValue<bool>() ?? false;

        _logger.LogInformation(
            "DataCollectNodeHandler [{Uuid}]: → data_collect (min={Min} max={Max} tries={Tries} var={Var} voice={Voice})",
            ctx.ChannelUuid, minDigits, maxDigits, maxTries, variableName, allowVoice);

        // uuid_transfer FIRST, voice media bug SECOND (if requested) — starting mod_audio_stream
        // before the transfer was found live (S150-adjacent testing) to corrupt its outbound
        // WebSocket handshake to the relay: uuid_transfer moving the channel's dialplan execution
        // into "data_collect" interrupts mod_audio_stream's not-yet-established connection, so it
        // never sends its correlation-token text frame — the relay then reads raw audio bytes as
        // the token and rejects it, and mod_audio_stream's client auto-reconnects and repeats this
        // forever (an infinite fast reconnect loop, never producing a real transcript). tf_ivr_menu's
        // voice path never hit this because it only ever uses uuid_broadcast alongside the media
        // bug, never uuid_transfer. Layering the media bug on AFTER the transfer has settled avoids
        // the interruption; a brief settle delay mirrors the WebRTC ICE/DTLS settle window pattern
        // used elsewhere (e.g. WhisperNodeHandler) for a freshly-changed channel state.
        await ctx.Esl.TransferAsync(ctx.ChannelUuid, "data_collect", "XML", "default", ct);

        if (allowVoice)
        {
            var sttProvider = await _sttStreaming.ResolveProviderAsync(ctx.TenantSchemaName, ct);
            if (sttProvider is null)
            {
                _logger.LogInformation(
                    "DataCollectNodeHandler [{Uuid}]: voice requested but tenant has no SttStreaming preference — DTMF-only", ctx.ChannelUuid);
            }
            else
            {
                await Task.Delay(300, ct);

                // Same RFC-based codec→rate derivation as IvrMenuNodeHandler.StartVoiceCaptureAsync
                // (S149) — mod_audio_stream's "8k"/"16k" label doesn't actually resample in this
                // build, so the STT vendor must be told the channel's real negotiated rate.
                var readCodec = await ctx.Esl.GetChannelVarAsync(ctx.ChannelUuid, "read_codec", ct);
                var sttSampleRateHz = ResolveSampleRateForCodec(readCodec);
                var sttSamplingRateLabel = sttSampleRateHz <= 8000 ? "8k" : "16k";

                var token = await _sttStreaming.PrepareCaptureAsync(
                    ctx.ChannelUuid, ctx.TenantSubdomain, sttProvider,
                    new Dictionary<string, string>(), new Dictionary<string, string>(),
                    null, timeoutMs, sttSampleRateHz, freeForm: true, ct: ct);

                var sttWsUrl = _config["FreeSWITCH:SttRelayWsUrl"] ?? "ws://host.docker.internal:5135/relay/stt-stream";
                // "mono" — mod_audio_stream taps the caller's own mic regardless of what's
                // simultaneously playing on the channel (the play_and_get_digits prompt already
                // running in the data_collect extension), same mix type tf_ivr_menu's voice option
                // uses.
                await ctx.Esl.StartAudioStreamAsync(ctx.ChannelUuid, sttWsUrl, "mono", sttSamplingRateLabel, token, ct);

                ctx.Vars["_dc_voice_active"] = "true";

                _logger.LogInformation(
                    "DataCollectNodeHandler [{Uuid}]: voice capture started via provider={Provider}",
                    ctx.ChannelUuid, sttProvider.ProviderKey);
            }
        }

        // Terminal — EslBackgroundService/SttStreamRelayEndpoints resume via
        // IDataCollectResolutionCoordinator once either side resolves.
        return new TelephonyNodeResult(null, "collecting");
    }

    /// <summary>Codec name → true sample rate — identical mapping to
    /// IvrMenuNodeHandler.ResolveSampleRateForCodec (S149); kept as its own copy here rather than
    /// shared since it's a tiny, self-contained RFC lookup and this avoids touching the
    /// already-live-verified tf_ivr_menu voice path.</summary>
    private static int ResolveSampleRateForCodec(string? codecName) => codecName?.Trim().ToLowerInvariant() switch
    {
        "opus" => 48000,
        "g722" => 16000,
        _ => 8000,
    };

    private static TelephonyNodeResult Follow(JsonObject? transitions, string preferredKey)
    {
        var target = transitions?[preferredKey]?.GetValue<string>()
                     ?? transitions?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(target, preferredKey);
    }
}
