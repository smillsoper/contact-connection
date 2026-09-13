using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_delay — pauses flow execution for a configured duration before continuing to a single
/// "default" transition. General-purpose pacing primitive (e.g. a beat between two prompts, or
/// paired with tf_repeat for a "wait N seconds between ring attempts" loop) — not specific to any
/// one flow shape.
///
/// Node config: <c>delayDurationMs</c> — a literal integer as text, or a <c>{{...}}</c> template
/// resolved via <see cref="TelSetVariableNodeHandler.Resolve"/> (the same resolver every other
/// telephony node uses), so a tenant can drive the wait from a campaign/flow variable instead of
/// a hardcoded value.
///
/// Implementation deliberately does NOT `await Task.Delay(durationMs)` on this thread — this
/// handler runs on the shared ESL event-processing path, and a multi-second in-process delay
/// there would stall every other call's event handling platform-wide (the exact class of bug
/// S128's playback-signal removal fixed for TTS). Instead this follows the same deferred-
/// continuation pattern as tf_ivr_menu / tf_voicemail / tf_play / tf_secure_collect: uuid_transfer
/// the channel into the "delay_wait" dialplan extension, which plays a *finite* silence_stream for
/// exactly the configured duration (blocking only that channel's own FreeSWITCH-side dialplan
/// thread — RTP keeps flowing the whole time, no dead air, no jitter-buffer starvation, per the
/// suppress-cng / auto-jitterbuffer-msec settings already in place), then emits
/// contactconnection::delay_done and re-parks. EslBackgroundService.HandleDelayDoneAsync resumes
/// the flow at the node's "default" transition from there.
/// </summary>
public class DelayNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_delay";

    // Sanity cap — a misconfigured or runaway {{variable}} shouldn't be able to park a channel
    // (and tie up its ESL session bookkeeping) indefinitely. 5 minutes is generously above any
    // legitimate pacing use case.
    private const int MaxDelayMs = 300_000;

    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly IEslCommanderFactory _eslFactory;
    private readonly ILogger<DelayNodeHandler> _logger;

    public DelayNodeHandler(
        ITelephonyCallSessionStore sessionStore,
        IEslCommanderFactory eslFactory,
        ILogger<DelayNodeHandler> logger)
    {
        _sessionStore = sessionStore;
        _eslFactory   = eslFactory;
        _logger       = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var transitions = node["transitions"]?.AsObject();
        var nextNodeId  = transitions?["default"]?.GetValue<string>();

        var raw      = node["delayDurationMs"]?.GetValue<string>() ?? "";
        var resolved = TelSetVariableNodeHandler.Resolve(raw, ctx);

        if (!int.TryParse(resolved, out var durationMs) || durationMs <= 0)
        {
            _logger.LogInformation(
                "DelayNodeHandler [{Uuid}]: no positive duration ('{Raw}' → '{Resolved}') — skipping wait",
                ctx.ChannelUuid, raw, resolved);
            return new TelephonyNodeResult(nextNodeId, "default");
        }

        if (durationMs > MaxDelayMs)
        {
            _logger.LogWarning(
                "DelayNodeHandler [{Uuid}]: requested {Requested}ms exceeds the {Max}ms cap — clamping",
                ctx.ChannelUuid, durationMs, MaxDelayMs);
            durationMs = MaxDelayMs;
        }

        // A fresh ESL for the transfer — ctx.Esl is null on an event branch.
        await using var owned = ctx.Esl is null ? await _eslFactory.CreateAsync(ct) : null;
        var esl = ctx.Esl ?? owned!;

        // Persist BEFORE starting the wait — delay_done can arrive within milliseconds of the
        // transfer for a short delay, and the CHANNEL_PARK guard / HandleDelayDoneAsync both read
        // this from the session, not from ctx.Vars (which only syncs to the session at the end of
        // a normal synchronous step — this node suspends before ever reaching that sync). Mirrors
        // the exact ordering fix S136 needed for tf_secure_collect's own suspend-before-park step.
        ctx.Vars["_delay_in_progress"]  = "true";
        ctx.Vars["_delay_next_node_id"] = nextNodeId ?? "";

        var session = await _sessionStore.GetAsync(ctx.ChannelUuid, ct);
        if (session is not null)
        {
            session.Vars["_delay_in_progress"]  = "true";
            session.Vars["_delay_next_node_id"] = nextNodeId ?? "";
            await _sessionStore.SaveAsync(session, ct);
        }

        await esl.SetChannelVarAsync(ctx.ChannelUuid, "cc_delay_ms", durationMs.ToString(), ct);
        await esl.TransferAsync(ctx.ChannelUuid, "delay_wait", "XML", "default", ct);

        _logger.LogInformation(
            "DelayNodeHandler [{Uuid}]: waiting {Ms}ms → {Next}",
            ctx.ChannelUuid, durationMs, nextNodeId ?? "(dead-end)");

        return new TelephonyNodeResult(null, "waiting");
    }
}
