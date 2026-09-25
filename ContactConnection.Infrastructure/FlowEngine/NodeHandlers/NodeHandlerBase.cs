using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Shared helpers for all node handlers — JSON access and state construction.
/// </summary>
public abstract class NodeHandlerBase
{
    protected readonly IVariableResolver Resolver;

    protected NodeHandlerBase(IVariableResolver resolver)
    {
        Resolver = resolver;
    }

    protected static string? Str(JsonObject node, string key) =>
        node[key]?.GetValue<string>();

    /// <summary>Parses a JSON array of GUID strings (e.g. add_to_cart/remove_cart_item's
    /// replacesOfferIds/removeOfferIds) — malformed or non-GUID entries are silently skipped.</summary>
    protected static List<Guid> GuidList(JsonObject node, string key)
    {
        if (node[key] is not JsonArray array) return [];
        return [.. array
            .Select(v => v?.GetValue<string>())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)];
    }

    protected static string StrReq(JsonObject node, string key) =>
        node[key]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Node is missing required property '{key}'.");

    protected static string? Transition(JsonObject node, string key = "default") =>
        node["transitions"]?[key]?.GetValue<string>();

    protected FlowNodeState BuildState(
        FlowExecutionContext ctx,
        JsonObject node,
        string resolvedContent,
        bool isTerminal = false,
        List<FlowOption>? options = null,
        string? inputType = null,
        string? condition = null,
        bool required = false) => new()
    {
        SessionId    = ctx.SessionId,
        NodeId       = ctx.CurrentNodeId,
        NodeType     = StrReq(node, "type"),
        Label        = Resolver.ResolveForDisplay(Str(node, "label") ?? string.Empty, ctx.ToVariableContext()),
        Content      = resolvedContent,
        IsTerminal   = isTerminal,
        Options      = options,
        InputType    = inputType,
        Condition    = condition,
        Required     = required,
        LockedFields = [.. ctx.LockedFields],
        WaitForTelephonyEventName = node["waitForTelephonyEventName"]?.GetValue<string>(),
        WaitForTelephonyEventTimeoutSeconds = node["waitForTelephonyEventTimeoutSeconds"]?.GetValue<int>()
    };

    protected static void AppendHistory(FlowExecutionContext ctx, JsonObject node, string? input, string? transition)
    {
        ctx.ExecutionHistory.Add(new NodeExecutionRecord(
            NodeId:         ctx.CurrentNodeId,
            NodeType:       Str(node, "type") ?? "unknown",
            Label:          Str(node, "label") ?? string.Empty,
            EnteredAt:      DateTimeOffset.UtcNow,
            InputValue:     input,
            TransitionTaken: transition));
    }
}
