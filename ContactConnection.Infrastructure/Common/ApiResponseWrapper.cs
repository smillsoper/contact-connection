using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Common;

/// <summary>
/// Builds the { success, status_code, status_message, response_headers, response, error,
/// timed_out } wrapper for a "general" API Definition call, in the two shapes both flow engines
/// need: a flat dot-path dictionary (for {{flow.myVar.response.field}} lookups against either
/// engine's flat variable store) and a single JSON blob (for {{flow.myVar}} bare references).
/// </summary>
internal static class ApiResponseWrapper
{
    /// <summary>Flat dot-path entries WITHOUT the variable-name prefix — callers write these
    /// into their flow's variable store under "{variableName}.{key}".</summary>
    public static Dictionary<string, string> BuildFlat(ApiDefinitionExecutionResult result)
    {
        var flat = new Dictionary<string, string>
        {
            ["success"]        = result.Success ? "true" : "false",
            ["status_code"]    = result.StatusCode?.ToString() ?? "",
            ["status_message"] = result.StatusMessage ?? "",
            ["error"]          = result.Error ?? "",
            ["timed_out"]      = result.TimedOut ? "true" : "false",
            ["response"]       = result.ResponseBody ?? "",
        };

        foreach (var (headerName, headerValue) in result.ResponseHeaders)
            flat[$"response_headers.{headerName}"] = headerValue;

        if (result.ResponseBody is not null)
        {
            try
            {
                var parsed = JsonNode.Parse(result.ResponseBody);
                if (parsed is JsonObject or JsonArray)
                    JsonFlattener.Flatten(parsed, "response", flat);
            }
            catch { /* not JSON — flat["response"] above still holds the raw text */ }
        }

        return flat;
    }

    /// <summary>The whole wrapper as one JSON blob, for {{flow.myVar}} bare references.</summary>
    public static string BuildJson(ApiDefinitionExecutionResult result)
    {
        JsonNode? responseNode = null;
        if (result.ResponseBody is not null)
        {
            try { responseNode = JsonNode.Parse(result.ResponseBody); }
            catch { responseNode = JsonValue.Create(result.ResponseBody); }
        }

        var wrapper = new JsonObject
        {
            ["success"]          = JsonValue.Create(result.Success),
            ["status_code"]      = result.StatusCode is int sc ? JsonValue.Create(sc) : null,
            ["status_message"]   = result.StatusMessage is string sm ? JsonValue.Create(sm) : null,
            ["response_headers"] = JsonSerializer.SerializeToNode(result.ResponseHeaders),
            ["response"]         = responseNode,
            ["error"]            = result.Error is string err ? JsonValue.Create(err) : null,
            ["timed_out"]        = JsonValue.Create(result.TimedOut),
        };
        return wrapper.ToJsonString();
    }
}
