using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_ivr_menu — plays a prompt and collects DTMF, branching per configured option.
///
/// FreeSWITCH does the heavy lifting: this sets <c>cc_ivr_*</c> channel vars and
/// <c>uuid_transfer</c>s the caller into the <c>ivr_collect</c> dialplan extension (this build
/// has no <c>uuid_execute</c>), which runs <c>play_and_get_digits</c> — built-in prompt playback,
/// barge-in, per-digit timeout, terminators, invalid re-prompt, retry count — then emits a
/// <c>CUSTOM contactconnection::ivr_done</c> event with the result and re-parks. The node returns
/// immediately; EslBackgroundService picks up that event, resolves the digits to a transition
/// target (see <see cref="IvrMenu"/>), and resumes the flow.
///
/// The extension <c>answer</c>s the channel, so tf_ivr_menu commits the call (no pre-answer
/// reject afterwards). Prompts must be audio files — play_and_get_digits' positional arg parser
/// can't take a TTS string with spaces.
///
/// Continuation state travels in session vars:
///   _ivr_in_progress = "true"   (also tells the CHANNEL_PARK handler the re-park isn't a new call)
///   _ivr_options     = JSON { "&lt;digits&gt;": "&lt;targetNodeId&gt;", … }
///   _ivr_no_match    = target node id for empty / unmatched input (may be empty)
///
/// <c>alwaysListen: true</c> — hot-digit mode (S141, motivated by "press 1 at any time while
/// waiting to opt into a callback"). Doesn't touch play_and_get_digits/ivr_collect at all: arms a
/// single-digit → node map in session state and returns via "default" immediately, so the flow
/// keeps going into whatever hold loop comes next. EslBackgroundService's raw DTMF event handler
/// (not ivr_done — no capture app is running) matches a press against the armed map from any
/// channel state and redirects. Only <c>options</c> (single-digit keys only) matter in this mode;
/// timeout/tries/prompt/no_match don't apply to an open-ended listener. Superseded by entering any
/// synchronous capture node (TelephonyFlowEngine, centrally) or bridging to an agent
/// (EslBackgroundService), or explicitly by the "Clear DTMF Listener" node (tf_clear_hot_digit).
/// </summary>
public class IvrMenuNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_ivr_menu";

    private readonly ITenantDbContextFactory _factory;
    private readonly ISttStreamingService _sttStreaming;
    private readonly IConfiguration _config;
    private readonly ILogger<IvrMenuNodeHandler> _logger;

    public IvrMenuNodeHandler(
        ITenantDbContextFactory factory, ISttStreamingService sttStreaming, IConfiguration config,
        ILogger<IvrMenuNodeHandler> logger)
    {
        _factory      = factory;
        _sttStreaming = sttStreaming;
        _config       = config;
        _logger       = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var transitions = node["transitions"]?.AsObject();

        if (node["alwaysListen"]?.GetValue<bool>() == true)
            return ArmHotDigitListener(node, ctx, transitions);

        if (ctx.Esl is null)
        {
            _logger.LogWarning("IvrMenuNodeHandler [{Uuid}]: no ESL connection — cannot collect DTMF", ctx.ChannelUuid);
            return Follow(transitions, "no_match");
        }

        var minDigits           = node["minDigits"]?.GetValue<int>() ?? 1;
        var maxDigits           = node["maxDigits"]?.GetValue<int>() ?? 1;
        var maxTries            = node["maxTries"]?.GetValue<int>() ?? 3;
        var timeoutMs           = node["timeoutMs"]?.GetValue<int>() ?? 5000;
        var interDigitTimeoutMs = node["interDigitTimeoutMs"]?.GetValue<int>() ?? 3000;
        var terminators         = node["terminators"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(terminators))
            terminators = maxDigits > 1 ? "#" : "none";

        // ── Options → { matchKey : targetNodeId } ───────────────────────────────
        // matchKey is the option's digit when it has one; phrase-only options (voice
        // recognition, S148 — see below) key by their own transition name instead, since
        // there's no digit to key by. A recognized phrase resolves through phraseIndex to the
        // same matchKey a keypress for that option would produce.
        var optionMap = new Dictionary<string, string>();
        var phraseIndex = new Dictionary<string, string>();
        var allPhrases = new List<string>();
        if (node["options"] is JsonArray opts)
        {
            foreach (var o in opts)
            {
                var digits = o?["digit"]?.GetValue<string>()?.Trim();
                var transitionKey = o?["transition"]?.GetValue<string>();
                var phrases = o?["phrases"] as JsonArray;
                if (string.IsNullOrEmpty(transitionKey)) continue;
                if (string.IsNullOrEmpty(digits) && (phrases is null || phrases.Count == 0)) continue;

                var target = transitions?[transitionKey]?.GetValue<string>();
                if (string.IsNullOrEmpty(target)) continue;

                var matchKey = !string.IsNullOrEmpty(digits) ? digits : transitionKey;
                optionMap[matchKey] = target;

                if (phrases is null) continue;
                foreach (var p in phrases)
                {
                    var phrase = p?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(phrase)) continue;
                    var normalized = IvrMenu.NormalizePhrase(phrase);
                    phraseIndex[normalized] = matchKey;
                    allPhrases.Add(normalized);
                }
            }
        }

        if (optionMap.Count == 0)
            _logger.LogWarning(
                "IvrMenuNodeHandler [{Uuid}]: no wired options — every entry will take no_match", ctx.ChannelUuid);

        var noMatchTarget = transitions?["no_match"]?.GetValue<string>() ?? string.Empty;

        // ── Resolve prompt media (audio file only) ─────────────────────────────
        var promptArg = await TelephonyAudioResolver.ResolveFileArgAsync(
            _factory, _config, node["promptAudioFileId"]?.GetValue<string>(), ctx.TenantSchemaName, ct);

        if (promptArg is null)
        {
            if (!string.IsNullOrWhiteSpace(node["promptTts"]?.GetValue<string>()))
                _logger.LogWarning(
                    "IvrMenuNodeHandler [{Uuid}]: TTS prompts aren't supported for IVR menus — configure an audio file", ctx.ChannelUuid);
            else
                _logger.LogWarning("IvrMenuNodeHandler [{Uuid}]: no prompt audio configured", ctx.ChannelUuid);
            return Follow(transitions, "no_match");
        }

        // Voice recognition (S148) — only when at least one option has phrases AND the tenant
        // has an SttStreaming vendor configured (ISttStreamingService.ResolveProviderAsync
        // returns null otherwise, same "no preference → fall back" shape PlayNodeHandler uses
        // for streaming TTS). Single-digit menus only: the DTMF side reuses the same raw-DTMF-
        // event mechanism the alwaysListen hot-digit listener already uses, which has no
        // inter-digit timer and so can't buffer a multi-digit sequence.
        if (allPhrases.Count > 0)
        {
            if (maxDigits > 1)
            {
                _logger.LogWarning(
                    "IvrMenuNodeHandler [{Uuid}]: phrases configured but maxDigits={MaxDigits} > 1 — " +
                    "voice recognition only supports single-digit menus; falling back to DTMF-only",
                    ctx.ChannelUuid, maxDigits);
            }
            else
            {
                var sttProvider = await _sttStreaming.ResolveProviderAsync(ctx.TenantSchemaName, ct);
                if (sttProvider is null)
                {
                    _logger.LogInformation(
                        "IvrMenuNodeHandler [{Uuid}]: phrases configured but tenant has no SttStreaming " +
                        "preference — falling back to DTMF-only", ctx.ChannelUuid);
                }
                else
                {
                    var nodeId = node["nodeId"]?.GetValue<string>() ?? "tf_ivr_menu";
                    return await StartVoiceCaptureAsync(
                        ctx, nodeId, sttProvider, promptArg, optionMap, phraseIndex, noMatchTarget, timeoutMs, ct);
                }
            }
        }

        var invalidArg = await TelephonyAudioResolver.ResolveFileArgAsync(
            _factory, _config, node["invalidAudioFileId"]?.GetValue<string>(), ctx.TenantSchemaName, ct)
            ?? "silence_stream://250";

        var regexp = IvrMenu.BuildRegexp(optionMap.Keys);

        // All values below are space-free — play_and_get_digits' arg parser is positional.
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_ivr_min", minDigits.ToString(), ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_ivr_max", maxDigits.ToString(), ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_ivr_tries", maxTries.ToString(), ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_ivr_timeout", timeoutMs.ToString(), ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_ivr_term", terminators, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_ivr_prompt", promptArg, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_ivr_invalid", invalidArg, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_ivr_regex", regexp, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_ivr_digit_timeout", interDigitTimeoutMs.ToString(), ct);

        ctx.Vars["_ivr_in_progress"] = "true";
        ctx.Vars["_ivr_options"]     = JsonSerializer.Serialize(optionMap);
        ctx.Vars["_ivr_no_match"]    = noMatchTarget;

        _logger.LogInformation(
            "IvrMenuNodeHandler [{Uuid}]: → ivr_collect (min={Min} max={Max} tries={Tries} regexp={Regexp} options=[{Opts}])",
            ctx.ChannelUuid, minDigits, maxDigits, maxTries, regexp, string.Join(",", optionMap.Keys));

        await ctx.Esl.TransferAsync(ctx.ChannelUuid, "ivr_collect", "XML", "default", ct);

        // Terminal — EslBackgroundService resumes from the contactconnection::ivr_done event.
        return new TelephonyNodeResult(null, "collecting");
    }

    /// <summary>
    /// Voice-recognition path (S148) — no dialplan extension, no play_and_get_digits/
    /// play_and_detect_speech, no uuid_transfer at all. Everything runs via direct ESL commands:
    /// answer, start the mod_audio_stream capture toward SttStreamRelayEndpoints, broadcast the
    /// prompt, and arm a raw-DTMF listener alongside it. Whichever of (DTMF press, matched
    /// phrase) resolves first wins via IIvrVoiceResolutionCoordinator — see
    /// EslBackgroundService.HandleDtmfAsync and SttStreamRelayEndpoints for the other halves of
    /// this race. Deliberately no retry/invalid-reprompt loop: one prompt, one listen window
    /// (timeoutMs, enforced by the relay's own task lifetime), then resolve or no_match.
    /// </summary>
    private async Task<TelephonyNodeResult> StartVoiceCaptureAsync(
        TelephonyFlowContext ctx, string nodeId, SttStreamingProviderInfo sttProvider, string promptArg,
        Dictionary<string, string> optionMap, Dictionary<string, string> phraseIndex,
        string noMatchTarget, int timeoutMs, CancellationToken ct)
    {
        await ctx.Esl!.AnswerChannelAsync(ctx.ChannelUuid, ct);

        // Derive the channel's REAL rate from its negotiated codec NAME, not the "read_rate"
        // channel var — live-verification (S149) showed read_rate is unreliable (reported 8000
        // on a call independently confirmed by ear to be real 48kHz Opus audio, no more
        // trustworthy than a hardcoded guess). Codec name → rate is fixed by RFC, not something
        // FreeSWITCH can misreport: Opus's SDP clock rate is always 48000 regardless of actual
        // bandwidth (RFC 7587); G.722 actually carries 16kHz audio despite an SDP clock rate
        // historically fixed at 8000 for backward compatibility (RFC 3551 §4.5.2); G.711 (PCMU/
        // PCMA) is genuinely 8000. mod_audio_stream's "8k"/"16k" sampling-rate label doesn't
        // actually resample in this build — it delivers native audio unchanged — so what matters
        // is declaring the TRUE rate to the STT vendor; the label passed to StartAudioStreamAsync
        // barely matters since it's not honored anyway, but must still be one of the module's two
        // accepted values.
        var readCodec = await ctx.Esl.GetChannelVarAsync(ctx.ChannelUuid, "read_codec", ct);
        var readRateDiag = await ctx.Esl.GetChannelVarAsync(ctx.ChannelUuid, "read_rate", ct);
        var sttSampleRateHz = ResolveSampleRateForCodec(readCodec);
        var sttSamplingRateLabel = sttSampleRateHz <= 8000 ? "8k" : "16k";

        _logger.LogInformation(
            "IvrMenuNodeHandler [{Uuid}]: read_codec={ReadCodec} read_rate={ReadRate} → declaring sampleRateHz={Rate} (label={Label})",
            ctx.ChannelUuid, readCodec ?? "(null)", readRateDiag ?? "(null)", sttSampleRateHz, sttSamplingRateLabel);

        var token = await _sttStreaming.PrepareCaptureAsync(
            ctx.ChannelUuid, ctx.TenantSubdomain, sttProvider, phraseIndex, optionMap,
            string.IsNullOrEmpty(noMatchTarget) ? null : noMatchTarget, timeoutMs, sttSampleRateHz, ct: ct);

        var sttWsUrl = _config["FreeSWITCH:SttRelayWsUrl"] ?? "ws://host.docker.internal:5135/relay/stt-stream";
        await ctx.Esl.StartAudioStreamAsync(ctx.ChannelUuid, sttWsUrl, "mono", sttSamplingRateLabel, token, ct);

        ctx.Vars["_ivr_voice_digit_options"] = JsonSerializer.Serialize(optionMap);
        ctx.Vars["_ivr_voice_node_id"]       = nodeId;

        await ctx.Esl.BroadcastAsync(ctx.ChannelUuid, promptArg, ct);

        _logger.LogInformation(
            "IvrMenuNodeHandler [{Uuid}]: voice capture started via provider={Provider} options=[{Opts}] phrases=[{Phrases}]",
            ctx.ChannelUuid, sttProvider.ProviderKey, string.Join(",", optionMap.Keys), string.Join(",", phraseIndex.Keys));

        // Terminal — resolution happens off the coordinator, from HandleDtmfAsync or the relay.
        return new TelephonyNodeResult(null, "collecting");
    }

    /// <summary>Arms a background single-digit listener and returns immediately via "default" —
    /// doesn't touch the channel at all (no prompt, no transfer). Single-digit options only:
    /// unlike the synchronous capture above, this has no inter-digit timer to buffer a sequence
    /// against arbitrary interleaved audio state, so a multi-character "digit" is rejected here
    /// rather than silently truncated or hung waiting for more input that will never come.</summary>
    private TelephonyNodeResult ArmHotDigitListener(JsonObject node, TelephonyFlowContext ctx, JsonObject? transitions)
    {
        var nodeId = node["nodeId"]?.GetValue<string>() ?? "tf_ivr_menu";
        var optionMap = new Dictionary<string, string>();

        if (node["options"] is JsonArray opts)
        {
            foreach (var o in opts)
            {
                var digit = o?["digit"]?.GetValue<string>()?.Trim();
                var transitionKey = o?["transition"]?.GetValue<string>();
                if (string.IsNullOrEmpty(digit) || string.IsNullOrEmpty(transitionKey)) continue;
                if (digit.Length != 1)
                {
                    _logger.LogWarning(
                        "IvrMenuNodeHandler [{Uuid}]: hot-digit option '{Digit}' is not a single character — skipped",
                        ctx.ChannelUuid, digit);
                    continue;
                }
                var target = transitions?[transitionKey]?.GetValue<string>();
                if (!string.IsNullOrEmpty(target))
                    optionMap[digit] = target;
            }
        }

        if (optionMap.Count == 0)
            _logger.LogWarning(
                "IvrMenuNodeHandler [{Uuid}]: hot-digit listener armed with no usable options — every press will be ignored",
                ctx.ChannelUuid);

        ctx.Vars["_hot_digit_options"] = JsonSerializer.Serialize(optionMap);
        ctx.Vars["_hot_digit_node_id"] = nodeId;

        _logger.LogInformation(
            "IvrMenuNodeHandler [{Uuid}]: hot-digit listener armed — digits=[{Digits}]",
            ctx.ChannelUuid, string.Join(",", optionMap.Keys));

        var next = transitions?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(next, "armed");
    }

    /// <summary>Codec name → true sample rate, per RFC (not something FreeSWITCH can misreport
    /// the way "read_rate" did — S149). Opus is always 48000 (RFC 7587) regardless of actual
    /// bandwidth; G.722 actually carries 16kHz despite an SDP clock rate fixed at 8000 for
    /// historical compatibility (RFC 3551 §4.5.2); everything else (G.711 PCMU/PCMA, missing/
    /// unrecognized codec) is the historical 8000 assumption.</summary>
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
