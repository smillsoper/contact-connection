using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Sets one or more named variables in the flow context.
/// Values support {{caller.ani}}, {{call.id}}, {{call.did}}, {{shared.*}}, or {{varName}} references.
/// Node data: { "assignments": [{ "key": "myVar", "value": "{{caller.ani}}" }] }
/// A "shared." key prefix (e.g. "shared.CC_Capture_Success") writes to the call-wide
/// ISharedCallVariableStore instead of this telephony call's own ctx.Vars — visible to the CRM
/// script flow for the same call as {{shared.*}} there too.
/// </summary>
public class TelSetVariableNodeHandler(ISharedCallVariableStore sharedVars) : ITelephonyNodeHandler
{
    public string NodeType => "tf_set_variable";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var assignments = node["assignments"]?.AsArray();
        if (assignments is not null)
        {
            foreach (var item in assignments)
            {
                if (item is not JsonObject a) continue;
                var key = a["key"]?.GetValue<string>();
                var rawValue = a["value"]?.GetValue<string>() ?? "";
                if (string.IsNullOrEmpty(key)) continue;
                var resolvedValue = Resolve(rawValue, ctx);

                if (key.StartsWith("shared.", StringComparison.OrdinalIgnoreCase))
                {
                    var sharedKey = key["shared.".Length..];
                    ctx.SharedVars[sharedKey] = resolvedValue;
                    await sharedVars.SetAsync(ctx.CallRecordId, sharedKey, resolvedValue, ct);
                }
                else
                {
                    ctx.Vars[key] = resolvedValue;
                }
            }
        }

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, "default");
    }

    internal static string Resolve(string template, TelephonyFlowContext ctx)
    {
        if (!template.Contains("{{"))
            return template;

        // Multi-token interpolation: replace every {{key}} in the string.
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{\{(.+?)\}\}", m =>
        {
            var key = m.Groups[1].Value.Trim();
            return ResolveKey(key, ctx);
        });
    }

    /// <summary>Exposed for handlers (e.g. QueueCallbackNodeHandler/ScheduledCallbackNodeHandler's
    /// "collected variable" field) that need to resolve a single already-unwrapped {{...}} key
    /// rather than interpolate it inside a larger string.</summary>
    internal static string ResolveKey(string key, TelephonyFlowContext ctx)
    {
        // Well-known namespaces
        if (key == "caller.ani")  return ctx.CallerNumber;
        if (key == "call.id")     return ctx.CallRecordId.ToString();
        if (key == "call.did")    return ctx.DestinationNumber;
        if (key == "call.dnis")   return ctx.DestinationNumber;    // alias

        if (key.StartsWith("now.", StringComparison.OrdinalIgnoreCase))
        {
            TimeZoneInfo tzi;
            try { tzi = TimeZoneInfo.FindSystemTimeZoneById(ctx.TenantTimezone); }
            catch { tzi = TimeZoneInfo.Utc; }

            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tzi);
            return key[4..].ToLowerInvariant() switch
            {
                "time"     => now.ToString("HH:mm"),
                "day_name" => now.DayOfWeek.ToString(),
                "date"     => now.ToString("yyyy-MM-dd"),
                "timezone" => tzi.Id,
                _ => string.Empty,
            };
        }

        // {{shared.varname}} — call-wide, also visible to the CRM script flow for the same call
        if (key.StartsWith("shared.", StringComparison.OrdinalIgnoreCase))
        {
            var sharedKey = key["shared.".Length..];
            return ctx.SharedVars.TryGetValue(sharedKey, out var sv) ? sv : string.Empty;
        }

        // {{flow.varname}} — strip the "flow." prefix and look up in Vars
        if (key.StartsWith("flow.", StringComparison.OrdinalIgnoreCase))
            key = key[5..];

        return ctx.Vars.TryGetValue(key, out var v) ? v : string.Empty;
    }
}
