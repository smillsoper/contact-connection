using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Infrastructure.StoredValues;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// tf_store_value — writes a resolved value into the generic tenant/client/campaign key-value
/// store (IStoredValueService), with an optional retention window. Telephony twin of the CRM
/// engine's StoreValueNodeHandler.
///
/// See SetCustomFieldNodeHandler for why TenantContext.Current must be primed here (background-
/// service execution, no ambient HTTP-request-scoped TenantContext).
/// </summary>
public class StoreValueNodeHandler(
    IStoredValueService storedValues, ITenantRepository tenantRepo, TenantContext tenantContext)
    : ITelephonyNodeHandler
{
    public string NodeType => "tf_store_value";

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var scope = node["scope"]?.GetValue<string>() ?? "tenant";
        var rawKey = node["keyName"]?.GetValue<string>() ?? "";
        var rawValue = node["value"]?.GetValue<string>() ?? "";

        var keyName = TelSetVariableNodeHandler.Resolve(rawKey, ctx).Trim();
        if (!string.IsNullOrEmpty(keyName))
        {
            var value = TelSetVariableNodeHandler.Resolve(rawValue, ctx);
            var expiresAt = RetentionOptions.ResolveExpiresAt(node["retention"]?.GetValue<string>());
            tenantContext.Current ??= await tenantRepo.GetByIdAsync(ctx.TenantId, ct);
            await storedValues.SetAsync(ctx.CallRecordId, scope, keyName, value, expiresAt, ct);
        }

        var nextNodeId = node["transitions"]?["default"]?.GetValue<string>();
        return new TelephonyNodeResult(nextNodeId, "default");
    }
}
