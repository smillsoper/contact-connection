using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "section" nodes — transparent auto-advancing nodes that mark named sections
/// of the flow and maintain section state in the execution context.
///
/// Node schema:
/// {
///   "type": "section",
///   "label": "Billing Information",
///   "name": "Billing Information",
///   "outputVariable": "section_billing",
///   "allowJumpFromAnywhere": false,
///   "clearPreviousValues": false,
///   "transitions": { "default": "node_002" }
/// }
///
/// The section is "locked" when flow.{outputVariable}.locked = "true" is set via set_variable.
/// Locked sections are traversed in read-only mode — agent sees values but cannot edit them.
/// </summary>
public class SectionNodeHandler(IVariableResolver resolver)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "section";

    public Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        // Record this section as encountered the moment the flow passes through it,
        // regardless of whether the agent completes it before jumping away.
        ctx.EncounteredSectionNodeIds.Add(ctx.CurrentNodeId);

        // Mark previous section as completed when entering a new section
        if (!string.IsNullOrEmpty(ctx.CurrentSectionNodeId))
            ctx.CompletedSectionNodeIds.Add(ctx.CurrentSectionNodeId);

        var name      = Str(node, "name") ?? Str(node, "label") ?? string.Empty;
        var outputVar = Str(node, "outputVariable")?.Trim() ?? string.Empty;

        // Check if this section is locked via its output variable
        var isLocked = false;
        if (!string.IsNullOrEmpty(outputVar) && ctx.FlowVars.TryGetValue(outputVar, out var varJson))
        {
            try
            {
                var obj = JsonNode.Parse(varJson)?.AsObject();
                isLocked = obj?["locked"]?.GetValue<string>() == "true";
            }
            catch { /* malformed — treat as unlocked */ }
        }

        ctx.CurrentSectionNodeId = ctx.CurrentNodeId;
        ctx.CurrentSectionName   = Resolver.Resolve(name, ctx.ToVariableContext());
        ctx.CurrentSectionLocked = isLocked;

        var next = Transition(node, agentTransition) ?? Transition(node, "default");
        AppendHistory(ctx, node, null, next);
        return Task.FromResult(new NodeResult(BuildState(ctx, node, resolvedContent: string.Empty), next));
    }
}
