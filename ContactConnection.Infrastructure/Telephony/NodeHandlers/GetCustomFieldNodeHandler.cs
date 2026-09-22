using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Infrastructure.CustomFields;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_get_custom_field — reads an existing Custom Field value for the current call record (scope-
/// resolved campaign > client > tenant, same as GetFieldsForCallAsync always does) and stores it
/// into a flow variable. Telephony twin of the CRM engine's GetCustomFieldNodeHandler. A field
/// with no value yet resolves to "" — not a failure, there's only one exit.
///
/// See SetCustomFieldNodeHandler for why TenantContext.Current must be primed here (background-
/// service execution, no ambient HTTP-request-scoped TenantContext).
/// </summary>
public class GetCustomFieldNodeHandler(
    ICustomFieldService customFields, ITenantRepository tenantRepo, TenantContext tenantContext)
    : ITelephonyNodeHandler
{
    public string NodeType => "tf_get_custom_field";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var definitionIdStr = node["definitionId"]?.GetValue<string>();
        var variableName = node["variableName"]?.GetValue<string>()?.Trim();

        if (!string.IsNullOrEmpty(variableName) &&
            !string.IsNullOrEmpty(definitionIdStr) && Guid.TryParse(definitionIdStr, out var definitionId))
        {
            tenantContext.Current ??= await tenantRepo.GetByIdAsync(ctx.TenantId, ct);

            var fields = await customFields.GetFieldsForCallAsync(ctx.CallRecordId, ct);
            var match = fields.FirstOrDefault(f => f.Definition.Id == definitionId);
            ctx.Vars[variableName] = CustomFieldValueFormatter.Format(match?.Value);
        }

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, "default");
    }
}
