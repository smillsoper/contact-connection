using System.Text.Json.Nodes;

namespace ContactConnection.Infrastructure.Common;

/// <summary>
/// Flattens a JSON tree into dot-path string keys (objects → "prefix.key", arrays →
/// "prefix.index"), written directly into a flat variable-store dictionary. Used to make API
/// response bodies addressable as {{flow.myVar.response.someField}} without either flow
/// engine's variable store needing to support nested objects.
/// </summary>
internal static class JsonFlattener
{
    public static void Flatten(JsonNode? node, string prefix, Dictionary<string, string> target)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                    Flatten(value, prefix.Length > 0 ? $"{prefix}.{key}" : key, target);
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                    Flatten(arr[i], $"{prefix}.{i}", target);
                break;
            case JsonValue val:
                target[prefix] = val.ToString();
                break;
            default:
                target[prefix] = string.Empty;
                break;
        }
    }
}
