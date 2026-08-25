using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Common;

/// <summary>
/// Redacts configured sensitive fields out of an API response body before it either (a) is shown
/// in the admin/portal "Test" button's response preview, or (b) gets written into flow variables
/// by a live api_call node — the latter is the real fix, since a live api_call node with an
/// outputVariable set persists the *entire raw vendor response* verbatim into
/// flow_sessions.variable_store (a real JSONB column) via ApiResponseWrapper. Both call sites
/// share this class so "what counts as sensitive" is one config, enforced the same way in both
/// places. See API_HARDENING_CHECKLIST.md Tier 3.
///
/// Deliberately minimal: dot-separated field paths only (no array indexing, no wildcards) — the
/// common case (SSN, DOB, card number) is almost always a top-level or nested object field, not
/// an array element. A path that doesn't resolve in a given response is silently a no-op, same
/// tolerance every other "field path" config in this codebase (response mapping, hmac payload
/// templates) uses for a response shape that doesn't match what was expected.
/// </summary>
public static class ResponseFieldMasker
{
    public const string RedactedPlaceholder = "[REDACTED]";

    /// <summary>Returns a copy of <paramref name="result"/> with sensitive fields in ResponseBody
    /// masked, per <paramref name="sensitiveFieldsJson"/> (a JSON array of dot-separated paths).
    /// Null/blank/malformed/empty-array config, or a null ResponseBody, returns
    /// <paramref name="result"/> unchanged (same instance) — masking is opt-in. Only ResponseBody
    /// is touched; StatusCode/Success/Error/ResponseHeaders are untouched.</summary>
    public static ApiDefinitionExecutionResult Mask(ApiDefinitionExecutionResult result, string? sensitiveFieldsJson)
    {
        if (result.ResponseBody is null) return result;
        var paths = ParsePaths(sensitiveFieldsJson);
        if (paths.Count == 0) return result;

        var masked = MaskJson(result.ResponseBody, paths);
        return ReferenceEquals(masked, result.ResponseBody) ? result : result with { ResponseBody = masked };
    }

    /// <summary>Masks a raw JSON response body string directly against a list of already-parsed
    /// field paths. Used by the admin/portal "Test" button path, which tests an in-progress
    /// (possibly still-unsaved) endpoint form rather than a persisted entity — the caller passes
    /// whatever sensitive-field config is currently in the form, not necessarily what's saved.
    /// Returns <paramref name="responseBody"/> unchanged if it isn't valid JSON (nothing
    /// structured to mask) or no paths are configured.</summary>
    public static string MaskJson(string responseBody, IReadOnlyList<string> fieldPaths)
    {
        if (fieldPaths.Count == 0) return responseBody;

        JsonNode? node;
        try { node = JsonNode.Parse(responseBody); }
        catch { return responseBody; }
        if (node is null) return responseBody;

        var changed = false;
        foreach (var path in fieldPaths)
            changed |= MaskPath(node, path);

        return changed ? node.ToJsonString() : responseBody;
    }

    /// <summary>Parses the stored/submitted JSON array of field paths, tolerating null/blank/
    /// malformed input the same way every other optional JSON config column in this codebase
    /// does — treat it as "nothing configured" rather than throwing.</summary>
    public static List<string> ParsePaths(string? sensitiveFieldsJson)
    {
        if (string.IsNullOrWhiteSpace(sensitiveFieldsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(sensitiveFieldsJson)
                ?.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToList() ?? [];
        }
        catch { return []; }
    }

    private static bool MaskPath(JsonNode root, string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return false;

        JsonNode? current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current is JsonObject obj ? obj[segments[i]] : null;
            if (current is null) return false; // path doesn't exist in this particular response
        }

        var lastKey = segments[^1];
        if (current is JsonObject leaf && leaf.ContainsKey(lastKey))
        {
            leaf[lastKey] = RedactedPlaceholder;
            return true;
        }
        return false;
    }
}
