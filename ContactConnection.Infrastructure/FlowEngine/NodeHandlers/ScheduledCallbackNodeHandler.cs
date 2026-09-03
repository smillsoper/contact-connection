using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Telephony;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "scheduled_callback" CRM-script nodes — an agent books a callback for the customer at
/// a future date/time while on the call. Writes a <see cref="ScheduledCallback"/> row; the
/// Worker's <c>ScheduledCallbackProcessingService</c> places the outbound call at that time and
/// routes the answered leg into <c>targetFlowId</c>. Executes and advances immediately
/// (transparent to the agent), like set_variable.
///
/// Node schema:
/// {
///   "type": "scheduled_callback",
///   "label": "Book a callback",
///   "callbackNumber": "{{flow.customer_phone.value}}",   // template — resolved now
///   "scheduledDateValue": "{{flow.cb_date}}",
///   "scheduledTimeValue": "{{flow.cb_time}}",            // blank => 09:00
///   "targetFlowId": "<telephony flow guid>",
///   "targetCampaignId": "<guid, optional>",
///   "allowedDays": "1,2,3,4,5", "allowedStartTime": "08:00", "allowedEndTime": "17:00",
///   "windowMinutes": 120, "maxAttempts": 3,
///   "callerIdOverride": "",                              // blank => DNIS the caller dialed
///   "outputVariable": "scheduled_callback",             // stores {id,status,scheduledFor}
///   "transitions": { "scheduled": "n1", "invalid_time": "n2", "failed": "n3" }
/// }
/// </summary>
public class ScheduledCallbackNodeHandler(
    IVariableResolver resolver,
    IScheduledCallbackRepository repo) : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "scheduled_callback";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var varCtx = ctx.ToVariableContext();

        var numberRaw = Resolver.Resolve(Str(node, "callbackNumber") ?? "", varCtx).Trim();
        var number    = ExtractNumber(numberRaw);
        if (string.IsNullOrWhiteSpace(number))
            return Advance(node, ctx, "failed", outputVar: Str(node, "outputVariable"), callback: null);

        var dateRaw = Resolver.Resolve(Str(node, "scheduledDateValue") ?? "", varCtx).Trim();
        var timeRaw = Resolver.Resolve(Str(node, "scheduledTimeValue") ?? "", varCtx).Trim();

        var window = new ScheduledCallbackTimeParser.AllowedWindow(
            Str(node, "allowedDays"), Str(node, "allowedStartTime"), Str(node, "allowedEndTime"));
        var (scheduledFor, outcome) = ScheduledCallbackTimeParser.Resolve(
            dateRaw, timeRaw, ctx.Tenant.GetValueOrDefault("timezone") ?? "UTC", window);

        if (outcome != ScheduledCallbackTimeParser.Ok)
            return Advance(node, ctx, outcome, outputVar: Str(node, "outputVariable"), callback: null);

        var callerIdRaw = Str(node, "callerIdOverride");
        var callerIdOverride = string.IsNullOrWhiteSpace(callerIdRaw) ? null : Resolver.Resolve(callerIdRaw, varCtx);

        Guid.TryParse(ctx.CallRecord.GetValueOrDefault("campaign_id"), out var campaignId);
        Guid.TryParse(Str(node, "targetFlowId"), out var targetFlowId);
        Guid.TryParse(Str(node, "targetCampaignId"), out var targetCampaignId);
        var dnis = ctx.CallRecord.GetValueOrDefault("dnis");

        var callback = ScheduledCallback.Create(
            ctx.TenantId, ctx.CallRecordId, campaignId, number, scheduledFor!.Value,
            node["windowMinutes"]?.GetValue<int>() ?? 120,
            node["maxAttempts"]?.GetValue<int>() ?? 3,
            callerIdOverride, dnis, targetFlowId, targetCampaignId);

        await repo.AddAsync(callback, ct);
        await repo.SaveChangesAsync(ct);

        return Advance(node, ctx, "scheduled", outputVar: Str(node, "outputVariable"), callback: callback);
    }

    private NodeResult Advance(
        JsonObject node, FlowExecutionContext ctx, string outcome, string? outputVar, ScheduledCallback? callback)
    {
        if (!string.IsNullOrWhiteSpace(outputVar))
        {
            var key = outputVar!.Trim();
            if (key.StartsWith("flow.", StringComparison.OrdinalIgnoreCase)) key = key[5..];
            ctx.FlowVars[key] = new JsonObject
            {
                ["outcome"]      = outcome,
                ["id"]           = callback?.Id.ToString(),
                ["status"]       = callback?.Status,
                ["scheduledFor"] = callback?.ScheduledFor.ToString("o"),
            }.ToJsonString();
        }

        var next = Transition(node, outcome) ?? Transition(node, "default");
        AppendHistory(ctx, node, input: null, transition: next);
        return new NodeResult(BuildState(ctx, node, resolvedContent: string.Empty), next);
    }

    /// <summary>The template may resolve to a phone-node JSON object, an object with a
    /// <c>value</c> field, or a plain string. Pull out the dialable digits; reject &lt; 7.</summary>
    private static string? ExtractNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var candidate = raw;
        try
        {
            if (JsonNode.Parse(raw) is JsonObject obj)
                candidate = obj["value"]?.GetValue<string>()
                            ?? obj["display_value"]?.GetValue<string>()
                            ?? raw;
        }
        catch { /* not JSON — use as-is */ }

        var digits = new string(candidate.Where(char.IsDigit).ToArray());
        return digits.Length >= 7 ? candidate.Trim() : null;
    }
}
