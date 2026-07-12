using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Infrastructure.FlowEngine;

namespace ContactConnection.Infrastructure.CallTrace;

/// <summary>
/// Builds the per-step "state at this point" snapshot stored on CallTraceEvent.StateSnapshot.
/// Redaction is keyed off a `"sensitive": true` flag on the node that owns a variable — this
/// flag doesn't exist on any node type yet (planned for input-capturing node types), so nothing
/// is redacted today. Once nodes gain that flag, values flowing through it are automatically
/// redacted here with no further changes needed.
/// </summary>
public static class CallTraceSnapshot
{
    private const string Redacted = "[REDACTED]";

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Scans a flow definition for nodes marked sensitive, returning the set of keys (node IDs
    /// and/or outputVariable names) whose values should be redacted before storage.
    /// </summary>
    public static HashSet<string> FindSensitiveKeys(JsonObject? flowDefinition)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodes = flowDefinition?["nodes"]?.AsObject();
        if (nodes is null) return keys;

        foreach (var (nodeId, nodeValue) in nodes)
        {
            if (nodeValue is not JsonObject node) continue;
            if (node["sensitive"] is not JsonValue sensitiveValue) continue;
            if (!sensitiveValue.TryGetValue<bool>(out var isSensitive) || !isSensitive) continue;

            keys.Add(nodeId);
            var outputVar = node["outputVariable"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(outputVar)) keys.Add(outputVar);
        }

        return keys;
    }

    private static Dictionary<string, string> Redact(
        IReadOnlyDictionary<string, string> vars, HashSet<string> sensitiveKeys) =>
        vars.ToDictionary(kv => kv.Key, kv => sensitiveKeys.Contains(kv.Key) ? Redacted : kv.Value);

    public static string BuildCrmSnapshot(FlowExecutionContext ctx, HashSet<string> sensitiveKeys) =>
        JsonSerializer.Serialize(new
        {
            flowVars = Redact(ctx.FlowVars, sensitiveKeys),
            inputs = Redact(ctx.Inputs, sensitiveKeys),
            apiResults = ctx.ApiResults,
            currentSection = ctx.CurrentSectionName,
            sectionLocked = ctx.CurrentSectionLocked,
            lockedFields = ctx.LockedFields,
        }, SerializeOptions);

    public static string BuildTelephonySnapshot(
        IReadOnlyDictionary<string, string> vars,
        IReadOnlyDictionary<string, string> sipHeaders,
        string callerNumber,
        string destinationNumber,
        string channelUuid) =>
        JsonSerializer.Serialize(new
        {
            vars,
            sipHeaders,
            callerNumber,
            destinationNumber,
            channelUuid,
        }, SerializeOptions);
}
