using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "set_custom_field" nodes — writes a resolved value into an existing Custom Field
/// definition for the current call record via ICustomFieldService, so it feeds reporting the same
/// way manually-configured custom fields do. Transparent to the agent; executes and advances
/// immediately.
///
/// Node schema:
/// {
///   "type": "set_custom_field",
///   "definitionId": "guid",
///   "value": "{{input.node_003}}",
///   "transitions": { "success": "node_010", "invalid_value": "node_011", "error": "node_012" }
/// }
/// </summary>
public class SetCustomFieldNodeHandler(IVariableResolver resolver, ICustomFieldService customFields)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "set_custom_field";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var varCtx = ctx.ToVariableContext();
        var definitionIdStr = Str(node, "definitionId");
        var rawValue = Str(node, "value") ?? string.Empty;

        string transitionKey;

        if (string.IsNullOrEmpty(definitionIdStr) || !Guid.TryParse(definitionIdStr, out var definitionId))
        {
            transitionKey = "error";
        }
        else
        {
            var resolvedValue = Resolver.Resolve(rawValue, varCtx);
            try
            {
                await customFields.SetValueAsync(ctx.CallRecordId, definitionId, resolvedValue, ct);
                transitionKey = "success";
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                transitionKey = "invalid_value";
            }
            catch (InvalidOperationException)
            {
                transitionKey = "error";
            }
        }

        var next = Transition(node, transitionKey) ?? Transition(node, "default");
        AppendHistory(ctx, node, input: null, transition: transitionKey);

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
