using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.CustomFields;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "get_custom_field" nodes — reads an existing Custom Field value for the current call
/// record (via ICustomFieldService, the same scope-resolved lookup GetFieldsForCallAsync already
/// does — campaign > client > tenant, most specific wins) and stores it into a flow variable.
/// Transparent to the agent; executes and advances immediately. A field with no value yet
/// resolves to "" — not a failure, there's only one exit.
///
/// Node schema:
/// {
///   "type": "get_custom_field",
///   "definitionId": "guid",
///   "outputVariable": "priorContactPreference",
///   "transitions": { "default": "node_010" }
/// }
/// </summary>
public class GetCustomFieldNodeHandler(IVariableResolver resolver, ICustomFieldService customFields)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "get_custom_field";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var definitionIdStr = Str(node, "definitionId");
        var variableName = Str(node, "outputVariable")?.Trim();

        if (!string.IsNullOrEmpty(variableName) &&
            !string.IsNullOrEmpty(definitionIdStr) && Guid.TryParse(definitionIdStr, out var definitionId))
        {
            var fields = await customFields.GetFieldsForCallAsync(ctx.CallRecordId, ct);
            var match = fields.FirstOrDefault(f => f.Definition.Id == definitionId);
            ctx.FlowVars[variableName] = CustomFieldValueFormatter.Format(match?.Value);
        }

        var next = Transition(node, "default");
        AppendHistory(ctx, node, input: null, transition: "default");

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }
}
