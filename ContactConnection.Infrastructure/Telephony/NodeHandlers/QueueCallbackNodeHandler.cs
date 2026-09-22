using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_queue_callback — "virtual hold". A caller who is already in queue opts out of holding but
/// KEEPS their queue position. The parked <see cref="TelephonyCallSession"/> stays in Redis as a
/// placeholder (this node does NOT touch <c>_queued</c> or <c>_in_queue_at</c>); the caller then
/// hangs up. When the placeholder reaches an available agent, <c>QueuePollingService</c> reserves
/// that agent (<see cref="AgentStateCodes.CallbackPending"/>), dials the caller back, plays a
/// connect prompt, and bridges the answered leg straight to the reserved agent — no re-queue, no
/// re-run of the inbound flow.
///
/// This is NOT a scheduled callback (no booked time, no Worker due-scan, no re-entering a flow).
///
/// Transitions:
///   queued  — opted in; wire to a Play ("we'll call you back when an agent is free — you can
///             hang up now") → Hang Up.
///   failed  — no usable callback number (withheld ANI and no collected variable).
///
/// Node config:
///   numberSource         "ani" (default) | "collected"
///   collectedVar         session/channel var to read when numberSource = "collected"
///   maxAttempts          outbound dial attempts before the callback is abandoned (default 3)
///   connectAudioFileId   audio played to the caller when the callback connects, before the
///                        bridge. Blank = a built-in "please hold, connecting you" prompt.
/// </summary>
public class QueueCallbackNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_queue_callback";

    private readonly ICallStateHistoryRecorder _callStateRecorder;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly ILogger<QueueCallbackNodeHandler> _logger;

    public QueueCallbackNodeHandler(
        ICallStateHistoryRecorder callStateRecorder,
        ITelephonyCallSessionStore sessionStore,
        ILogger<QueueCallbackNodeHandler> logger)
    {
        _callStateRecorder = callStateRecorder;
        _sessionStore      = sessionStore;
        _logger            = logger;
    }

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var transitions = node["transitions"]?.AsObject();

        var number = ResolveNumber(node, ctx);
        if (string.IsNullOrWhiteSpace(number))
        {
            _logger.LogWarning(
                "QueueCallbackNodeHandler [{Uuid}]: no callback number available — 'failed'", ctx.ChannelUuid);
            return Follow(transitions, "failed");
        }

        var maxAttempts  = Math.Max(1, node["maxAttempts"]?.GetValue<int>() ?? 3);
        var connectAudio = node["connectAudioFileId"]?.GetValue<string>() ?? "";

        // The `failed` transition target. Reused if the callback connects but the bridge to the
        // reserved agent then fails (QueueCallbackDeliveryService re-queues the caller and resumes
        // the flow here) — so the tenant's queue-MOH / alternate-destination wiring gives the
        // caller real audio instead of dead air. Blank when the branch isn't wired.
        var failedNode = transitions?["failed"]?.GetValue<string>()
                         ?? transitions?["default"]?.GetValue<string>()
                         ?? "";

        // Mark the session as a queue-callback placeholder. _queued / _in_queue_at are left
        // untouched so the placeholder keeps its position and its acceleration clock. _left_for_
        // callback reuses the CHANNEL_HANGUP guard so the caller's hangup is not logged as an
        // in-queue abandon.
        ctx.Vars["_queue_callback"]              = "true";
        ctx.Vars["_queue_callback_number"]       = number;
        ctx.Vars["_queue_callback_max_attempts"] = maxAttempts.ToString();
        ctx.Vars["_queue_callback_attempts"]     = "0";
        ctx.Vars["_queue_callback_connect_audio"] = connectAudio;
        ctx.Vars["_queue_callback_failed_node"]  = failedNode;
        ctx.Vars["_left_for_callback"]           = "true";

        // The engine's var-sync copies ctx.Vars into the session, but a queue-callback node runs
        // from the ivr_done / PLAYBACK_STOP resume path — mirror ScheduledCallbackNodeHandler and
        // write the session directly too so the placeholder is durable the instant the caller
        // hangs up (which can race the resume sync).
        var session = await _sessionStore.GetAsync(ctx.ChannelUuid, ct);
        if (session is not null)
        {
            session.Vars["_queue_callback"]               = "true";
            session.Vars["_queue_callback_number"]        = number;
            session.Vars["_queue_callback_max_attempts"]  = maxAttempts.ToString();
            session.Vars["_queue_callback_attempts"]      = "0";
            session.Vars["_queue_callback_connect_audio"] = connectAudio;
            session.Vars["_queue_callback_failed_node"]   = failedNode;
            session.Vars["_left_for_callback"]            = "true";
            await _sessionStore.SaveAsync(session, ct);
        }

        await _callStateRecorder.RecordAsync(
            ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId,
            CallHistoryState.PostAgent, ctx.CampaignId, agentId: null,
            detail: $"Queue callback requested → {number} (position held)", ct: ct);

        _logger.LogInformation(
            "QueueCallbackNodeHandler [{Uuid}]: virtual hold → {Number} (maxAttempts {Max}); queue position kept",
            ctx.ChannelUuid, number, maxAttempts);

        return Follow(transitions, "queued");
    }

    private static string? ResolveNumber(JsonObject node, TelephonyFlowContext ctx)
    {
        var source = node["numberSource"]?.GetValue<string>() ?? "ani";

        if (string.Equals(source, "collected", StringComparison.OrdinalIgnoreCase))
        {
            var value = ResolveCollectedNumber(node["collectedVar"]?.GetValue<string>(), ctx);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        var ani = ctx.CallerNumber?.Trim();
        if (string.IsNullOrWhiteSpace(ani)) return null;
        var digits = new string(ani.Where(char.IsDigit).ToArray());
        return digits.Length >= 7 ? ani : null;
    }

    /// <summary>
    /// The designer's "Number variable" field is documented as a bare var name (placeholder
    /// "e.g. callback_digits"), but every other value field in the telephony designer uses
    /// {{flow.x}}/{{shared.x}}/{{caller.ani}} template syntax — an easy, reasonable mix-up.
    /// Accepts either: a bare name (or "{{name}}"/"{{flow.name}}") is looked up in ctx.Vars, then
    /// ctx.ChannelVars (e.g. a SIP header extracted earlier in the call); any other {{...}}
    /// namespace (shared./caller./call./now.) resolves through the same
    /// TelSetVariableNodeHandler.ResolveKey every other value field uses.
    /// </summary>
    private static string ResolveCollectedNumber(string? raw, TelephonyFlowContext ctx)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var key = raw.Trim();
        if (key.StartsWith("{{") && key.EndsWith("}}"))
            key = key[2..^2].Trim();

        if (key.StartsWith("shared.", StringComparison.OrdinalIgnoreCase) ||
            key is "caller.ani" or "call.did" or "call.dnis" ||
            key.StartsWith("now.", StringComparison.OrdinalIgnoreCase))
            return TelSetVariableNodeHandler.ResolveKey(key, ctx);

        var name = key.StartsWith("flow.", StringComparison.OrdinalIgnoreCase) ? key[5..] : key;
        if (ctx.Vars.TryGetValue(name, out var v)) return v;
        if (ctx.ChannelVars.TryGetValue(name, out var cv)) return cv;
        return "";
    }

    private static TelephonyNodeResult Follow(JsonObject? transitions, string preferredKey)
    {
        var target = transitions?[preferredKey]?.GetValue<string>()
                     ?? transitions?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(target, preferredKey);
    }
}
