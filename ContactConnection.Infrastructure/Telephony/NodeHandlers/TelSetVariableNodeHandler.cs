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
        if (!template.StartsWith("{{") || !template.EndsWith("}}"))
            return template;

        var key = template[2..^2].Trim();
        return key switch
        {
            "caller.ani" => ctx.CallerNumber,
            "call.did"   => ctx.DestinationNumber,
            _ => ctx.Vars.TryGetValue(key, out var v) ? v : string.Empty,
        };
    }
}
