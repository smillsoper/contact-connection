using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine;

/// <summary>
/// Resolves {{namespace.field}} template tags against a VariableContext.
///
/// Tag format: {{namespace.field}} where namespace is one of:
///   call_record, caller, agent, tenant, input, api, flow, shared
///
/// For input and api namespaces the field itself contains the node_id:
///   {{input.node_001}}          → Inputs["node_001"]
///   {{api.node_005.customerId}} → ApiResults["node_005.customerId"]
/// </summary>
public partial class VariableResolver : IVariableResolver
{
    // Matches {{ ... }} including nested dots — e.g. {{api.node_005.customerId}}
    [GeneratedRegex(@"\{\{([^}]+)\}\}", RegexOptions.Compiled)]
    private static partial Regex TagPattern();

    public string Resolve(string template, VariableContext context) =>
        ResolveInternal(template, context, missingFallback: string.Empty);

    public string ResolveForDisplay(string template, VariableContext context) =>
        ResolveInternal(template, context, missingFallback: "[not captured]");

    private string ResolveInternal(string template, VariableContext context, string missingFallback)
    {
        if (string.IsNullOrEmpty(template)) return template;

        return TagPattern().Replace(template, match =>
        {
            var tag = match.Groups[1].Value.Trim();
            return ResolveTag(tag, context) ?? missingFallback;
        });
    }

    public IEnumerable<string> ExtractReferences(string template)
    {
        if (string.IsNullOrEmpty(template)) return [];
        return TagPattern().Matches(template)
            .Select(m => m.Groups[1].Value.Trim())
            .Distinct();
    }

    public bool EvaluateCondition(string condition, VariableContext context)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;

        // Resolve any tags in the condition first
        var resolved = Resolve(condition, context);

        // Try each operator in order (longest first to avoid prefix conflicts)
        if (TryEvaluate(resolved, "contains", StringContains)) return StringContains(resolved, "contains");
        if (TryMatch(resolved, "!=", out var l, out var r)) return !string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        if (TryMatch(resolved, ">=", out l, out r)) return CompareNumeric(l, r) >= 0;
        if (TryMatch(resolved, "<=", out l, out r)) return CompareNumeric(l, r) <= 0;
        if (TryMatch(resolved, "==", out l, out r)) return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        if (TryMatch(resolved, ">", out l, out r)) return CompareNumeric(l, r) > 0;
        if (TryMatch(resolved, "<", out l, out r)) return CompareNumeric(l, r) < 0;

        // Bare value — truthy if non-empty and not "false"/"0"
        return IsTruthy(resolved.Trim());
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a variable key out of a flat dictionary, supporting dot-notation access into
    /// stored JSON objects. "phone" → returns the raw JSON string stored under "phone".
    /// "phone.isTollFree" → parses the JSON under "phone" and extracts ["isTollFree"]. Shared by
    /// the flow.* and shared.* namespaces — both are flat string dictionaries with the same
    /// JSON-object-value convention.
    /// </summary>
    private static string? ResolveDictVar(Dictionary<string, string> dict, string key)
    {
        if (dict.TryGetValue(key, out var direct))
            return direct;

        var dotIdx = key.IndexOf('.');
        if (dotIdx <= 0) return null;

        var objKey = key[..dotIdx];
        var prop   = key[(dotIdx + 1)..];
        if (!dict.TryGetValue(objKey, out var json)) return null;

        try
        {
            var node = JsonNode.Parse(json);
            var val  = node?[prop];
            return val is null ? null : val.ToString();
        }
        catch { return null; }
    }

    private string? ResolveTag(string tag, VariableContext context)
    {
        // Split on first dot only for namespace extraction
        var dotIndex = tag.IndexOf('.');
        if (dotIndex < 0) return null;

        var ns = tag[..dotIndex].ToLowerInvariant();
        var key = tag[(dotIndex + 1)..];

        return ns switch
        {
            "call_record" => context.CallRecord.GetValueOrDefault(key),
            "caller"      => context.Caller.GetValueOrDefault(key),
            "agent"       => context.Agent.GetValueOrDefault(key),
            "tenant"      => context.Tenant.GetValueOrDefault(key),
            "flow"        => ResolveDictVar(context.FlowVars, key),
            "input"       => context.Inputs.GetValueOrDefault(key),
            "api"         => context.ApiResults.GetValueOrDefault(key),
            "shared"      => ResolveDictVar(context.SharedVars, key),
            _             => null
        };
    }

    private static bool TryMatch(string expression, string op, out string left, out string right)
    {
        var idx = expression.IndexOf(op, StringComparison.Ordinal);
        if (idx < 0) { left = right = string.Empty; return false; }
        left  = Unquote(expression[..idx].Trim());
        right = Unquote(expression[(idx + op.Length)..].Trim());
        return true;
    }

    private static bool TryEvaluate(string expression, string op, Func<string, string, bool> eval)
    {
        var idx = expression.IndexOf(op, StringComparison.OrdinalIgnoreCase);
        return idx >= 0;
    }

    private static bool StringContains(string expression, string op)
    {
        var idx = expression.IndexOf(op, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        var left  = Unquote(expression[..idx].Trim());
        var right = Unquote(expression[(idx + op.Length)..].Trim());
        return left.Contains(right, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareNumeric(string left, string right)
    {
        if (decimal.TryParse(left, out var l) && decimal.TryParse(right, out var r))
            return l.CompareTo(r);
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    private static bool IsTruthy(string s) =>
        !string.IsNullOrEmpty(s) &&
        !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) &&
        s != "0";
}
