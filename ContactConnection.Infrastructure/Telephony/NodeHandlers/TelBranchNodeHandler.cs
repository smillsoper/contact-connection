using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Evaluates a simple condition against flow variables.
/// Condition format: "{{flow.varName}} operator value" (or any tag TelSetVariableNodeHandler.Resolve
/// supports: bare {{varName}}, {{caller.ani}}, {{call.did}}, {{now.*}}).
/// Operators: ==, !=, >, <, >=, <=, contains
/// </summary>
public class TelBranchNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_branch";

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var condition = node["condition"]?.GetValue<string>() ?? "";
        var result = EvaluateCondition(condition, ctx);
        var transition = result ? "true" : "false";
        var nextNodeId = node["transitions"]?[transition]?.GetValue<string>()
                      ?? node["transitions"]?["default"]?.GetValue<string>();

        return Task.FromResult(new TelephonyNodeResult(nextNodeId, transition));
    }

    private static bool EvaluateCondition(string condition, TelephonyFlowContext ctx)
    {
        if (string.IsNullOrWhiteSpace(condition)) return false;

        // Resolve {{...}} tags using the same resolver every other telephony node uses —
        // handles {{flow.varname}} (and bare {{varname}}), {{caller.ani}}, {{call.did}},
        // {{now.*}}. Previously this did its own raw ctx.Vars lookup keyed on the full tag
        // text (including "flow."), which never matched since ctx.Vars keys never carry that
        // prefix — every {{flow.*}} condition silently resolved to "" and fell through to false.
        var resolved = TelSetVariableNodeHandler.Resolve(condition, ctx);

        // Split into: left operator right
        string[] ops = [">=", "<=", "!=", "==", ">", "<", "contains"];
        foreach (var op in ops)
        {
            var idx = resolved.IndexOf(op, StringComparison.Ordinal);
            if (idx < 0) continue;

            var left = resolved[..idx].Trim().Trim('"');
            var right = resolved[(idx + op.Length)..].Trim().Trim('"');

            return op switch
            {
                "==" => left == right,
                "!=" => left != right,
                "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
                ">" when double.TryParse(left, out var l) && double.TryParse(right, out var r) => l > r,
                "<" when double.TryParse(left, out var l) && double.TryParse(right, out var r) => l < r,
                ">=" when double.TryParse(left, out var l) && double.TryParse(right, out var r) => l >= r,
                "<=" when double.TryParse(left, out var l) && double.TryParse(right, out var r) => l <= r,
                _ => false
            };
        }

        return false;
    }
}
