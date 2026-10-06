using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "set_disposition" nodes (S181) — records this interaction's disposition from the disposition catalog, the
/// purpose-built replacement for "Set Call Record Value → disposition". Transparent to the agent; executes and advances.
///
/// The disposition is either a catalog entry chosen in the designer (<c>dispositionId</c> — its CURRENT name is written, so
/// a rename in the catalog flows through) or a resolved template (<c>value</c>, e.g. a question's answer). It's written the
/// same way scripts always have — the "disposition" custom field, on this interaction for a transferred call — so the
/// completion sync links it to the catalog for KPIs, commissions, retention and exports. Also set as
/// <c>{{flow.disposition}}</c>, which is the fallback when the call has no disposition field in scope.
///
/// Node schema:
/// {
///   "type": "set_disposition",
///   "dispositionId": "guid",            // or
///   "value": "{{input.node_003}}",
///   "transitions": { "success": "node_010", "error": "node_011" }
/// }
/// </summary>
public class SetDispositionNodeHandler(
    IVariableResolver resolver, ICustomFieldService customFields, ScopedTenantDbContextFactory dbFactory)
    : NodeHandlerBase(resolver), INodeHandler
{
    public const string FieldName = "disposition";

    public string NodeType => "set_disposition";

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var text = await DispositionTextAsync(node, ctx, ct);
        string transitionKey;

        if (string.IsNullOrWhiteSpace(text))
        {
            transitionKey = "error";
        }
        else
        {
            ctx.FlowVars[FieldName] = text;
            try
            {
                var field = (await customFields.GetFieldsForCallAsync(ctx.CallRecordId, ct))
                    .FirstOrDefault(f => f.Definition.FieldName == FieldName);
                // Per interaction on a transferred call (S178) — see ICustomFieldService.SetValueFromScriptAsync.
                if (field is not null)
                    await customFields.SetValueFromScriptAsync(ctx.CallRecordId, ctx.InteractionId, field.Definition.Id, text, ct);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
            {
                // No disposition field in this interaction's scope — {{flow.disposition}} (set above) is what the
                // completion step reads instead, so the disposition still lands.
            }
            transitionKey = "success";
        }

        var next = Transition(node, transitionKey) ?? Transition(node, "default");
        AppendHistory(ctx, node, input: text, transition: transitionKey);
        return new NodeResult(BuildState(ctx, node, resolvedContent: string.Empty), next);
    }

    private async Task<string?> DispositionTextAsync(JsonObject node, FlowExecutionContext ctx, CancellationToken ct)
    {
        if (Guid.TryParse(Str(node, "dispositionId"), out var id))
        {
            await using var db = dbFactory.Create();
            var name = await db.Dispositions.AsNoTracking().Where(d => d.Id == id).Select(d => d.Name).FirstOrDefaultAsync(ct);
            // A deleted catalog entry: fall back to the name saved on the node.
            return name ?? Str(node, "dispositionName");
        }
        var raw = Str(node, "value");
        return string.IsNullOrWhiteSpace(raw) ? null : Resolver.Resolve(raw, ctx.ToVariableContext()).Trim();
    }
}
