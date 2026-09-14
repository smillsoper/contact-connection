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
    private readonly IConfiguration _config;
    private readonly ILogger<IvrMenuNodeHandler> _logger;

    public IvrMenuNodeHandler(
        ITenantDbContextFactory factory, IConfiguration config, ILogger<IvrMenuNodeHandler> logger)
    {
        _factory = factory;
        _config  = config;
        _logger  = logger;
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

        // ── Options → { digits : targetNodeId } ─────────────────────────────────
        var optionMap = new Dictionary<string, string>();
        if (node["options"] is JsonArray opts)
        {
            foreach (var o in opts)
            {
                var digits = o?["digit"]?.GetValue<string>()?.Trim();
                var transitionKey = o?["transition"]?.GetValue<string>();
                if (string.IsNullOrEmpty(digits) || string.IsNullOrEmpty(transitionKey)) continue;
                var target = transitions?[transitionKey]?.GetValue<string>();
                if (!string.IsNullOrEmpty(target))
                    optionMap[digits] = target;
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

    private static TelephonyNodeResult Follow(JsonObject? transitions, string preferredKey)
    {
        var target = transitions?[preferredKey]?.GetValue<string>()
                     ?? transitions?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(target, preferredKey);
    }
}
