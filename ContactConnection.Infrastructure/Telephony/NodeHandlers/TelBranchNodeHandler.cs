using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Evaluates a simple condition against flow variables.
/// Condition format: "{{varName}} operator value"
/// Operators: ==, !=, >, <, >=, <=, contains
/// </summary>
public class TelBranchNodeHandler : ITelephonyNodeHandler
{
    public string NodeType => "tf_branch";

    public Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var condition = node["condition"]?.GetValue<string>() ?? "";
        var result = EvaluateCondition(condition, ctx.Vars);
        var transition = result ? "true" : "false";
        var nextNodeId = node["transitions"]?[transition]?.GetValue<string>()
                      ?? node["transitions"]?["default"]?.GetValue<string>();

        return Task.FromResult(new TelephonyNodeResult(nextNodeId, transition));
    }

    private static bool EvaluateCondition(string condition, Dictionary<string, string> vars)
    {
        if (string.IsNullOrWhiteSpace(condition)) return false;

        // Resolve {{varName}} placeholders
        var resolved = System.Text.RegularExpressions.Regex.Replace(
            condition,
            @"\{\{([^}]+)\}\}",
            m => vars.TryGetValue(m.Groups[1].Value.Trim(), out var v) ? v : "");

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
