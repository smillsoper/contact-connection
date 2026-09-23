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

    /// <summary>
    /// Resolves a field that historically accepted a bare variable name (no {{...}} wrapper) —
    /// e.g. a "Number variable" or "Check variable" designer field — while every OTHER value
    /// field in the telephony designer uses {{flow.x}}/{{shared.x}}/{{caller.ani}} template
    /// syntax. A user pattern-matching from those other fields who types "{{flow.x}}" into a
    /// bare-name-only field gets a silent failed-lookup with no error (this exact bug, first
    /// found in QueueCallback/ScheduledCallback's "collectedVar" field — see DevLog Session 153).
    ///
    /// Accepts either shape: a bare name (or "{{name}}"/"{{flow.name}}") is looked up in
    /// ctx.Vars, then ctx.ChannelVars (e.g. a SIP header extracted earlier in the call); any
    /// other {{...}} namespace (shared./caller./call./now.) resolves through <see cref="ResolveKey"/>,
    /// the same resolution every other value field uses.
    /// </summary>
    internal static string ResolveNameOrTemplate(string? raw, TelephonyFlowContext ctx)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var key = raw.Trim();
        if (key.StartsWith("{{") && key.EndsWith("}}"))
            key = key[2..^2].Trim();

        if (key.StartsWith("shared.", StringComparison.OrdinalIgnoreCase) ||
            key is "caller.ani" or "call.id" or "call.did" or "call.dnis" ||
            key.StartsWith("now.", StringComparison.OrdinalIgnoreCase))
            return ResolveKey(key, ctx);

        var name = key.StartsWith("flow.", StringComparison.OrdinalIgnoreCase) ? key[5..] : key;
        if (ctx.Vars.TryGetValue(name, out var v)) return v;
        if (ctx.ChannelVars.TryGetValue(name, out var cv)) return cv;
        return "";
    }
}
