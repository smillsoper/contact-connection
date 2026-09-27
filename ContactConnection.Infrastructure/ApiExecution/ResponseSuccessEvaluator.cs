using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// Applies an endpoint's SuccessCriteria to a result the HTTP layer already considered a success
/// (2xx) — for APIs that report failure in the response body. Criteria JSON:
///   {"rules":[{"path":"success","operator":"equals","value":"true"}], "errorMessagePath":"message"}
/// All rules must pass. Paths are dot-separated, array elements by index ("errors.0").
/// Operators: equals, not_equals, contains, exists, not_exists, truthy, falsy. Comparison is
/// case-insensitive on the value's text form (so "true" matches a JSON true).
/// On failure the result becomes Success=false with Error taken from errorMessagePath (falling back
/// to a description of the failed rule), and the response body is kept for the flow to inspect.
/// </summary>
public static class ResponseSuccessEvaluator
{
    private record Rule(string? Path, string? Operator, JsonNode? Value);

    public static ApiDefinitionExecutionResult Apply(ApiDefinitionExecutionResult result, string? criteriaJson)
    {
        if (!result.Success || string.IsNullOrWhiteSpace(criteriaJson) || criteriaJson.Trim() == "{}")
            return result;

        JsonObject? criteria;
        try { criteria = JsonNode.Parse(criteriaJson) as JsonObject; }
        catch (JsonException) { return result; } // invalid criteria never breaks a call — validated on save
        if (criteria?["rules"] is not JsonArray rules || rules.Count == 0) return result;

        JsonNode? body;
        try { body = string.IsNullOrWhiteSpace(result.ResponseBody) ? null : JsonNode.Parse(result.ResponseBody); }
        catch (JsonException)
        {
            return result with { Success = false, Error = "Response was not JSON, so its success rules could not be checked." };
        }

        foreach (var ruleNode in rules.OfType<JsonObject>())
        {
            var rule = new Rule(ruleNode["path"]?.GetValue<string>(), ruleNode["operator"]?.GetValue<string>(), ruleNode["value"]);
            if (Passes(body, rule, out var failure)) continue;

            var messagePath = criteria["errorMessagePath"]?.GetValue<string>();
            var vendorMessage = string.IsNullOrWhiteSpace(messagePath) ? null : Text(Select(body, messagePath));
            return result with
            {
                Success = false,
                Error = string.IsNullOrWhiteSpace(vendorMessage) ? failure : vendorMessage,
            };
        }
        return result;
    }

    /// <summary>Null when the criteria JSON is well-formed; otherwise what's wrong — used when an
    /// endpoint is saved.</summary>
    public static string? Validate(string? criteriaJson)
    {
        if (string.IsNullOrWhiteSpace(criteriaJson)) return null;
        try
        {
            if (JsonNode.Parse(criteriaJson) is not JsonObject o) return "Success criteria must be a JSON object.";
            if (o["rules"] is null) return null;
            if (o["rules"] is not JsonArray rules) return "\"rules\" must be an array.";
            foreach (var r in rules)
            {
                if (r is not JsonObject ro || string.IsNullOrWhiteSpace(ro["path"]?.GetValue<string>()))
                    return "Each rule needs a \"path\".";
                var op = ro["operator"]?.GetValue<string>() ?? "equals";
                if (op is not ("equals" or "not_equals" or "contains" or "exists" or "not_exists" or "truthy" or "falsy"))
                    return $"Unknown operator \"{op}\".";
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return $"Invalid success criteria: {ex.Message}";
        }
    }

    private static bool Passes(JsonNode? body, Rule rule, out string failure)
    {
        var path = rule.Path ?? "";
        var op = rule.Operator ?? "equals";
        var actualNode = Select(body, path);
        var actual = Text(actualNode);
        var expected = Text(rule.Value) ?? "";
        failure = $"Response check failed: {path} {op.Replace('_', ' ')} {expected}".TrimEnd() + $" (was: {actual ?? "missing"})";

        return op switch
        {
            "exists"     => actualNode is not null,
            "not_exists" => actualNode is null,
            "truthy"     => IsTruthy(actual),
            "falsy"      => !IsTruthy(actual),
            "not_equals" => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            "contains"   => actual?.Contains(expected, StringComparison.OrdinalIgnoreCase) ?? false,
            _            => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
        };
    }

    internal static JsonNode? Select(JsonNode? node, string path)
    {
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node switch
            {
                JsonObject o => o[segment],
                JsonArray a when int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                                 && i >= 0 && i < a.Count => a[i],
                _ => null,
            };
            if (node is null) return null;
        }
        return node;
    }

    private static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        JsonValue v when v.GetValueKind() == JsonValueKind.Null => null,
        _ => node.ToJsonString(),
    };

    private static bool IsTruthy(string? s) =>
        !string.IsNullOrEmpty(s) && s is not ("false" or "False" or "0" or "null" or "[]" or "{}" or "\"\"");
}
