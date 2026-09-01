using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_voicemail — plays a greeting, beeps, and records the caller's message, then continues the
/// flow on <c>recorded</c> (message captured) or <c>no_message</c> (caller left nothing / hung
/// up before the minimum length).
///
/// Same shape as tf_ivr_menu: this build has no <c>uuid_execute</c>, so the node sets
/// <c>cc_vm_*</c> channel vars and <c>uuid_transfer</c>s the caller into the <c>vm_record</c>
/// dialplan extension, which runs <c>playback</c> (greeting) → <c>playback</c> (beep) →
/// <c>record</c> (built-in silence detection + max length), then emits
/// <c>CUSTOM contactconnection::vm_done</c> with the recorded path + duration and re-parks.
/// <c>EslBackgroundService.HandleVmDoneAsync</c> stores the audio in blob storage, writes the
/// <c>Voicemail</c> row, fires the optional email delivery + supervisor SignalR push, and resumes
/// the flow.
///
/// The extension <c>answer</c>s the channel, so tf_voicemail commits the call.
///
/// Continuation state travels in session vars:
///   _vm_in_progress   = "true"   (also tells the CHANNEL_PARK handler the re-park isn't a new call)
///   _vm_node_id       = this node's id  (vm_done re-reads the delivery config from the cached flow def)
///   _vm_next_recorded / _vm_next_no_message = transition targets
///   _vm_min_ms        = minimum recording length in ms, below which no_message is taken
///   _vm_path          = container path of the .wav
/// </summary>
public class VoicemailNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_voicemail";

    private readonly ITenantDbContextFactory _factory;
    private readonly IConfiguration _config;
    private readonly ILogger<VoicemailNodeHandler> _logger;

    public VoicemailNodeHandler(
        ITenantDbContextFactory factory, IConfiguration config, ILogger<VoicemailNodeHandler> logger)
    {
        _factory = factory;
        _config  = config;
        _logger  = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var transitions = node["transitions"]?.AsObject();

        if (ctx.Esl is null)
        {
            _logger.LogWarning(
                "VoicemailNodeHandler [{Uuid}]: no ESL connection — cannot record a message", ctx.ChannelUuid);
            return Follow(transitions, "no_message");
        }

        var maxLengthSeconds = node["maxLengthSeconds"]?.GetValue<int>() ?? 120;
        var maxSilenceSecs   = node["maxSilenceSeconds"]?.GetValue<int>() ?? 5;
        var minLengthSeconds = node["minLengthSeconds"]?.GetValue<int>() ?? 2;
        var beepEnabled      = node["beepEnabled"]?.GetValue<bool>() ?? true;
        var silenceThreshold = int.TryParse(_config["Voicemail:SilenceThreshold"], out var st) && st > 0 ? st : 200;

        var recordingsBase = (_config["FreeSWITCH:RecordingsContainerPath"] ?? "/var/lib/freeswitch/recordings")
            .TrimEnd('/');
        var path = $"{recordingsBase}/{ctx.CallRecordId}-vm.wav";

        // ── Greeting: audio file first, TTS fallback (mirrors tf_record's consent) ──
        var greetingArg = await TelephonyAudioResolver.ResolveFileArgAsync(
            _factory, _config, node["greetingAudioFileId"]?.GetValue<string>(), ctx.TenantSchemaName, ct);

        if (greetingArg is null)
        {
            var tts = node["greetingTtsText"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(tts))
            {
                var voice = node["greetingTtsVoice"]?.GetValue<string>() ?? "kal";
                await ctx.Esl.SetChannelVarAsync(
                    ctx.ChannelUuid, "cc_vm_greeting_text", tts.Replace("\n", " ").Trim(), ct);
                greetingArg = $"tts://flite|{voice}|${{cc_vm_greeting_text}}";
            }
            else
            {
                greetingArg = "silence_stream://500";
            }
        }

        var beepArg = beepEnabled ? "tone_stream://%(500,0,800)" : "silence_stream://100";

        // record app args are positional: <path> <time_limit> <silence_threshold> <silence_hits(secs)>
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_vm_greeting", greetingArg, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_vm_beep", beepArg, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_vm_path", path, ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_vm_maxlen", maxLengthSeconds.ToString(), ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_vm_silence_thresh", silenceThreshold.ToString(), ct);
        await ctx.Esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_vm_silence_secs", maxSilenceSecs.ToString(), ct);

        ctx.Vars["_vm_in_progress"]     = "true";
        ctx.Vars["_vm_node_id"]         = node["nodeId"]?.GetValue<string>() ?? string.Empty;
        ctx.Vars["_vm_next_recorded"]   = transitions?["recorded"]?.GetValue<string>() ?? string.Empty;
        ctx.Vars["_vm_next_no_message"] = transitions?["no_message"]?.GetValue<string>() ?? string.Empty;
        ctx.Vars["_vm_min_ms"]          = (Math.Max(0, minLengthSeconds) * 1000).ToString();
        ctx.Vars["_vm_path"]            = path;

        _logger.LogInformation(
            "VoicemailNodeHandler [{Uuid}]: → vm_record (maxLen={Max}s silence={Sil}s min={Min}s beep={Beep})",
            ctx.ChannelUuid, maxLengthSeconds, maxSilenceSecs, minLengthSeconds, beepEnabled);

        await ctx.Esl.TransferAsync(ctx.ChannelUuid, "vm_record", "XML", "default", ct);

        // Terminal — HandleVmDoneAsync resumes from the contactconnection::vm_done event.
        return new TelephonyNodeResult(null, "recording");
    }

    private static TelephonyNodeResult Follow(JsonObject? transitions, string preferredKey)
    {
        var target = transitions?[preferredKey]?.GetValue<string>()
                     ?? transitions?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(target, preferredKey);
    }
}
