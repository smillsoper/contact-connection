using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Sets one or more named variables in the flow context.
/// Values support {{caller.ani}}, {{call.did}}, or {{varName}} references.
/// Node data: { "assignments": [{ "key": "myVar", "value": "{{caller.ani}}" }] }
/// </summary>
public class TelSetVariableNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_set_variable";

    public Task<TelephonyNodeResult> ExecuteAsync(
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
                ctx.Vars[key] = Resolve(rawValue, ctx);
            }
        }

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return Task.FromResult(new TelephonyNodeResult(nextNodeId, "default"));
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

    private static string ResolveKey(string key, TelephonyFlowContext ctx)
    {
        // Well-known namespaces
        if (key == "caller.ani")  return ctx.CallerNumber;
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

        // {{flow.varname}} — strip the "flow." prefix and look up in Vars
        if (key.StartsWith("flow.", StringComparison.OrdinalIgnoreCase))
            key = key[5..];

        return ctx.Vars.TryGetValue(key, out var v) ? v : string.Empty;
    }
}
