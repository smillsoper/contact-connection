using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_request_callback — a queued caller opts out of holding and asks to be called back. Records
/// a <see cref="Callback"/> row (status <c>scheduled</c>), takes the caller out of the queue so
/// QueuePollingService stops offering the call to agents, and continues the flow on
/// <c>requested</c> (wire this to a Play "we'll call you back shortly" → Hangup) or, if no
/// callback number is available, <c>failed</c> (wire back to the hold-music / queue path).
///
/// The outbound leg is placed later by the Worker's <c>CallbackProcessingService</c> when the
/// window opens; that service also handles retries, expiry, and marking the abandon.
///
/// Node config:
///   numberSource   — "ani" (default; use the caller's presented number) or "collected"
///                    (read <c>collectedVar</c> from session/channel vars — e.g. digits a prior
///                    tf_ivr_menu / tf_dtmf node captured)
///   collectedVar   — session/channel var name when numberSource = "collected"
///   delayMinutes   — how far out the callback window opens (default 0 = as soon as possible)
///   windowMinutes  — how long the window stays open before the request expires (default 120)
///   maxAttempts    — outbound attempts before the callback is abandoned (default 3)
///   callerIdOverride — caller ID for the outbound leg; blank = the DNIS the caller dialed.
///                    A literal number or a {{variable}} (resolved to a literal here, so it's
///                    frozen at request time). Carrier CID rules still apply — must be a number
///                    on the trunk's account.
/// </summary>
public class RequestCallbackNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_request_callback";

    private readonly ITenantDbContextFactory _factory;
    private readonly ICallStateHistoryRecorder _callStateRecorder;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly ILogger<RequestCallbackNodeHandler> _logger;

    public RequestCallbackNodeHandler(
        ITenantDbContextFactory factory,
        ICallStateHistoryRecorder callStateRecorder,
        ITelephonyCallSessionStore sessionStore,
        ILogger<RequestCallbackNodeHandler> logger)
    {
        _factory           = factory;
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
                "RequestCallbackNodeHandler [{Uuid}]: no callback number available — taking 'failed'", ctx.ChannelUuid);
            return Follow(transitions, "failed");
        }

        var delay         = TimeSpan.FromMinutes(Math.Max(0, node["delayMinutes"]?.GetValue<int>() ?? 0));
        var windowMinutes = node["windowMinutes"]?.GetValue<int>() ?? 120;
        var maxAttempts   = node["maxAttempts"]?.GetValue<int>() ?? 3;

        var callerIdRaw   = node["callerIdOverride"]?.GetValue<string>();
        var callerIdOverride = string.IsNullOrWhiteSpace(callerIdRaw)
            ? null
            : TelSetVariableNodeHandler.Resolve(callerIdRaw, ctx);

        // ctx.DestinationNumber is the DID the caller dialed (routing.Number) — freeze it on the
        // row so the callback presents the right caller ID and routes back to the same DID, no
        // matter how many DIDs the campaign has.
        var callback = Callback.Create(
            ctx.TenantId, ctx.CallRecordId, ctx.CampaignId, number, delay, windowMinutes, maxAttempts,
            callerIdOverride, dnis: ctx.DestinationNumber);

        await using (var db = _factory.Create(ctx.TenantSchemaName))
        {
            db.Callbacks.Add(callback);
            await db.SaveChangesAsync(ct);
        }

        // Out of the queue. The flow engine's var-sync only copies ctx.Vars INTO the session — it
        // never deletes keys — so removing _queued here alone would leave the persisted session
        // still marked queued: QueuePollingService could bridge this caller to an agent mid
        // "we'll call you back" audio, and the CHANNEL_HANGUP handler would log a spurious
        // in_queue abandon that double-counts the callback. Clear it on the session directly and
        // stamp _left_for_callback so the hangup handler skips the abandon classification.
        ctx.Vars.Remove("_queued");
        ctx.Vars["_callback_id"]        = callback.Id.ToString();
        ctx.Vars["_left_for_callback"]  = "true";

        var session = await _sessionStore.GetAsync(ctx.ChannelUuid, ct);
        if (session is not null)
        {
            session.Vars.Remove("_queued");
            session.Vars["_callback_id"]       = callback.Id.ToString();
            session.Vars["_left_for_callback"] = "true";
            await _sessionStore.SaveAsync(session, ct);
        }

        // Routing state: the caller is leaving the queue with a callback booked. Recorded as a
        // routing transition with detail (NOT an abandon — a callback only becomes an abandon if
        // the caller never answers it, which CallbackProcessingService records at that point).
        await _callStateRecorder.RecordAsync(
            ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId,
            CallHistoryState.PostAgent, ctx.CampaignId, agentId: null,
            detail: $"Callback requested → {number} (callback {callback.Id})", ct: ct);

        _logger.LogInformation(
            "RequestCallbackNodeHandler [{Uuid}]: callback {CallbackId} scheduled for {Number} " +
            "(window {Window}m, maxAttempts {Max})",
            ctx.ChannelUuid, callback.Id, number, windowMinutes, callback.MaxAttempts);

        return Follow(transitions, "requested");
    }

    private static string? ResolveNumber(JsonObject node, TelephonyFlowContext ctx)
    {
        var source = node["numberSource"]?.GetValue<string>() ?? "ani";

        if (string.Equals(source, "collected", StringComparison.OrdinalIgnoreCase))
        {
            var varName = node["collectedVar"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(varName))
            {
                if (ctx.Vars.TryGetValue(varName, out var v) && !string.IsNullOrWhiteSpace(v))
                    return v.Trim();
                if (ctx.ChannelVars.TryGetValue(varName, out var cv) && !string.IsNullOrWhiteSpace(cv))
                    return cv.Trim();
            }
            return null;
        }

        // "ani" — the caller's presented number. Reject obvious non-numbers (anonymous / withheld).
        var ani = ctx.CallerNumber?.Trim();
        if (string.IsNullOrWhiteSpace(ani)) return null;
        var digits = new string(ani.Where(char.IsDigit).ToArray());
        return digits.Length >= 7 ? ani : null;
    }

    private static TelephonyNodeResult Follow(JsonObject? transitions, string preferredKey)
    {
        var target = transitions?[preferredKey]?.GetValue<string>()
                     ?? transitions?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(target, preferredKey);
    }
}
