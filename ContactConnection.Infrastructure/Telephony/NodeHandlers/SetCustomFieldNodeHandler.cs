using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_set_custom_field — writes a resolved value into an existing Custom Field definition for the
/// current call record via ICustomFieldService, so it feeds reporting the same way manually-
/// configured custom fields do. Telephony twin of the CRM engine's SetCustomFieldNodeHandler.
///
/// Runs from a background service (EslBackgroundService), not an HTTP request — like
/// ScriptPopNodeHandler, ICustomFieldService's repositories resolve their tenant DB context off
/// ambient TenantContext.Current, which TenantResolutionMiddleware only populates on the HTTP
/// path. Primed here from ctx.TenantId before calling the service.
/// </summary>
public class SetCustomFieldNodeHandler(
    ICustomFieldService customFields, ITenantRepository tenantRepo, TenantContext tenantContext)
    : ITelephonyNodeHandler
{
    public string NodeType => "tf_set_custom_field";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var definitionIdStr = node["definitionId"]?.GetValue<string>();
        var rawValue = node["value"]?.GetValue<string>() ?? string.Empty;

        string transitionKey;

        if (string.IsNullOrEmpty(definitionIdStr) || !Guid.TryParse(definitionIdStr, out var definitionId))
        {
            transitionKey = "error";
        }
        else
        {
            var resolvedValue = TelSetVariableNodeHandler.Resolve(rawValue, ctx);
            tenantContext.Current ??= await tenantRepo.GetByIdAsync(ctx.TenantId, ct);

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

        var nextNodeId = node["transitions"]?[transitionKey]?.GetValue<string>()
                       ?? node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, transitionKey);
    }
}
