using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_scheduled_callback — books a callback for a specific future time. The tenant's flow
/// captures the desired date and time however it likes (IVR, DTMF, agent entry) and passes them
/// as text; this node resolves any <c>{{variable}}</c>, parses them in the tenant timezone,
/// validates against an optional allowed day/hour window, and writes a <see cref="ScheduledCallback"/>
/// row (status <c>scheduled</c>). The Worker's <c>ScheduledCallbackProcessingService</c> places
/// the outbound leg when the time comes; the answered leg routes into <c>targetFlowId</c>.
///
/// This is NOT a queue callback / virtual hold — that is a separate feature.
///
/// Transitions:
///   scheduled     — row written
///   invalid_time  — date/time parsed but is in the past or outside the allowed window
///   failed        — no callback number, or the date/time could not be parsed
///
/// Node config:
///   numberSource        "ani" (default) | "collected" (read collectedVar from session/channel vars)
///   collectedVar        var name when numberSource = "collected"
///   scheduledDateValue  text or {{variable}} — the date (e.g. "2026-09-10", "9/10/2026")
///   scheduledTimeValue  text or {{variable}} — the time (e.g. "14:30", "2:30 PM"); blank => 09:00
///   targetFlowId        telephony flow the answered leg runs (should NOT re-offer callback)
///   targetCampaignId    optional campaign context for the answered leg's queue
///   allowedDays         optional CSV of 0-6 (0=Sun) the callback may land on
///   allowedStartTime    optional "HH:mm" — earliest local time-of-day
///   allowedEndTime      optional "HH:mm" — latest local time-of-day
///   windowMinutes       how long past the booked time the worker keeps trying (default 120)
///   maxAttempts         outbound attempts before abandon (default 3)
///   callerIdOverride    outbound caller ID; blank = the DNIS the caller dialed. Literal or
///                       {{variable}}, frozen at request time. Carrier CID rules apply.
/// </summary>
public class ScheduledCallbackNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_scheduled_callback";

    private readonly ITenantDbContextFactory _factory;
    private readonly ICallStateHistoryRecorder _callStateRecorder;
    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly ILogger<ScheduledCallbackNodeHandler> _logger;

    public ScheduledCallbackNodeHandler(
        ITenantDbContextFactory factory,
        ICallStateHistoryRecorder callStateRecorder,
        ITelephonyCallSessionStore sessionStore,
        ILogger<ScheduledCallbackNodeHandler> logger)
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
                "ScheduledCallbackNodeHandler [{Uuid}]: no callback number available — 'failed'", ctx.ChannelUuid);
            return Follow(transitions, "failed");
        }

        var dateRaw = TelSetVariableNodeHandler.Resolve(node["scheduledDateValue"]?.GetValue<string>() ?? "", ctx).Trim();
        var timeRaw = TelSetVariableNodeHandler.Resolve(node["scheduledTimeValue"]?.GetValue<string>() ?? "", ctx).Trim();

        var window = new ScheduledCallbackTimeParser.AllowedWindow(
            node["allowedDays"]?.GetValue<string>(),
            node["allowedStartTime"]?.GetValue<string>(),
            node["allowedEndTime"]?.GetValue<string>());
        var (scheduledFor, outcome) = ScheduledCallbackTimeParser.Resolve(dateRaw, timeRaw, ctx.TenantTimezone, window);
        if (outcome != ScheduledCallbackTimeParser.Ok)
        {
            _logger.LogInformation(
                "ScheduledCallbackNodeHandler [{Uuid}]: date='{Date}' time='{Time}' → '{Outcome}'",
                ctx.ChannelUuid, dateRaw, timeRaw, outcome);
            return Follow(transitions, outcome);
        }

        var windowMinutes = node["windowMinutes"]?.GetValue<int>() ?? 120;
        var maxAttempts   = node["maxAttempts"]?.GetValue<int>() ?? 3;

        var callerIdRaw = node["callerIdOverride"]?.GetValue<string>();
        var callerIdOverride = string.IsNullOrWhiteSpace(callerIdRaw)
            ? null
            : TelSetVariableNodeHandler.Resolve(callerIdRaw, ctx);

        Guid.TryParse(node["targetFlowId"]?.GetValue<string>(), out var targetFlowId);
        Guid.TryParse(node["targetCampaignId"]?.GetValue<string>(), out var targetCampaignId);

        var callback = ScheduledCallback.Create(
            ctx.TenantId, ctx.CallRecordId, ctx.CampaignId, number, scheduledFor!.Value,
            windowMinutes, maxAttempts, callerIdOverride, dnis: ctx.DestinationNumber,
            targetFlowId: targetFlowId, targetCampaignId: targetCampaignId);

        await using (var db = _factory.Create(ctx.TenantSchemaName))
        {
            db.ScheduledCallbacks.Add(callback);
            await db.SaveChangesAsync(ct);
        }

        // If the caller was queued, take them out — the flow engine's var-sync only copies
        // ctx.Vars INTO the session (never deletes), so clear _queued on the session directly and
        // stamp _left_for_callback so the CHANNEL_HANGUP handler doesn't log an in_queue abandon.
        ctx.Vars.Remove("_queued");
        ctx.Vars["_scheduled_callback_id"] = callback.Id.ToString();
        ctx.Vars["_left_for_callback"]     = "true";

        var session = await _sessionStore.GetAsync(ctx.ChannelUuid, ct);
        if (session is not null)
        {
            session.Vars.Remove("_queued");
            session.Vars["_scheduled_callback_id"] = callback.Id.ToString();
            session.Vars["_left_for_callback"]     = "true";
            await _sessionStore.SaveAsync(session, ct);
        }

        await _callStateRecorder.RecordAsync(
            ctx.TenantId, ctx.TenantSchemaName, ctx.CallRecordId,
            CallHistoryState.PostAgent, ctx.CampaignId, agentId: null,
            detail: $"Callback scheduled → {number} at {scheduledFor:u} (callback {callback.Id})", ct: ct);

        _logger.LogInformation(
            "ScheduledCallbackNodeHandler [{Uuid}]: callback {CallbackId} → {Number} at {When:u} " +
            "(window {Window}m, maxAttempts {Max}, targetFlow {Flow})",
            ctx.ChannelUuid, callback.Id, number, scheduledFor, windowMinutes, callback.MaxAttempts,
            targetFlowId == Guid.Empty ? "(campaign default)" : targetFlowId);

        return Follow(transitions, "scheduled");
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
