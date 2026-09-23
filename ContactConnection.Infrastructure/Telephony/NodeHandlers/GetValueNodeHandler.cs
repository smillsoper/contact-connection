using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_get_value — reads a value back from the generic tenant/client/campaign key-value store
/// (IStoredValueService) into a flow variable. Telephony twin of the CRM engine's
/// GetValueNodeHandler. No value stored yet (or expired) resolves to "" — not a failure, there's
/// only one exit.
///
/// See SetCustomFieldNodeHandler for why TenantContext.Current must be primed here.
/// </summary>
public class GetValueNodeHandler(
    IStoredValueService storedValues, ITenantRepository tenantRepo, TenantContext tenantContext)
    : ITelephonyNodeHandler
{
    public string NodeType => "tf_get_value";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var scope = node["scope"]?.GetValue<string>() ?? "tenant";
        var rawKey = node["keyName"]?.GetValue<string>() ?? "";
        var variableName = node["variableName"]?.GetValue<string>()?.Trim();

        if (!string.IsNullOrEmpty(variableName))
        {
            var keyName = TelSetVariableNodeHandler.Resolve(rawKey, ctx).Trim();
            string? value = null;
            if (!string.IsNullOrEmpty(keyName))
            {
                tenantContext.Current ??= await tenantRepo.GetByIdAsync(ctx.TenantId, ct);
                value = await storedValues.GetAsync(ctx.CallRecordId, scope, keyName, ct);
            }
            ctx.Vars[variableName] = value ?? string.Empty;
        }

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, "default");
    }
}
