using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Common;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Telephony.NodeHandlers;

/// <summary>
/// Handles "tf_general_api_call" nodes — the telephony-engine equivalent of the CRM engine's
/// "api_call" node. Invokes a saved endpoint of a "general"-category API Definition (tenant or
/// portal scoped) — the definition is a connection (base URL, auth, timeout), the endpoint is
/// the individual callable operation — and stores the { success, status_code, status_message,
/// response_headers, response, error, timed_out } wrapper flattened into ctx.Vars under
/// outputVariable.
///
/// Runs from a background service (EslBackgroundService), not an HTTP request, so — like
/// CheckBlockListNodeHandler — it cannot use the tenant repositories/credential store that
/// depend on ambient TenantContext; it goes straight to ITenantDbContextFactory / the
/// credential store's explicit-tenant overload instead.
///
/// Node schema: identical to the CRM "api_call" node (apiEndpointId, apiDefinitionScope,
/// outputVariable, timeoutSeconds, transitions: success/error/timeout).
/// </summary>
public class GeneralApiCallNodeHandler(
    ITenantDbContextFactory tenantDbFactory,
    ContactConnectionDbContext portalDb,
    ITenantCredentialStore tenantCredentials,
    IPortalCredentialStore portalCredentials,
    IApiDefinitionExecutor executor)
    : ITelephonyNodeHandler
{
    public string NodeType => "tf_general_api_call";

    private record CallTarget(
        Guid DefinitionId, string HttpMethod, string BaseUrl, string Path, string Headers, string QueryParams,
        string? RequestBodyTemplate, string AuthConfig, int TimeoutSeconds, bool IsActive, bool IsRetrySafe,
        int? RateLimitPerMinute, string SensitiveResponseFields);

    public async Task<TelephonyNodeResult> ExecuteAsync(
        JsonObject node, TelephonyFlowContext ctx, CancellationToken ct = default)
    {
        var outputVariable = node["outputVariable"]?.GetValue<string>()?.Trim();
        var scope = node["apiDefinitionScope"]?.GetValue<string>() ?? "tenant";
        var endpointIdStr = node["apiEndpointId"]?.GetValue<string>();

        ApiDefinitionExecutionResult result;
        string transitionKey;
        var targetSensitiveFields = "[]";

        if (string.IsNullOrEmpty(endpointIdStr) || !Guid.TryParse(endpointIdStr, out var endpointId))
        {
            result = new ApiDefinitionExecutionResult(
                false, null, null, new(), null, false,
                "API Call node is not configured with an API endpoint.");
            transitionKey = "error";
        }
        else
        {
            var target = scope == "portal"
                ? await LoadPortalAsync(endpointId, ct)
                : await LoadTenantAsync(ctx.TenantSchemaName, endpointId, ct);
            targetSensitiveFields = target?.SensitiveResponseFields ?? "[]";

            if (target is null)
            {
                result = new ApiDefinitionExecutionResult(
                    false, null, null, new(), null, false, "API endpoint not found.");
                transitionKey = "error";
            }
            else if (!target.IsActive)
            {
                result = new ApiDefinitionExecutionResult(
                    false, null, null, new(), null, false, "API endpoint or its definition is not active.");
                transitionKey = "error";
            }
            else
            {
                var resolvedBaseUrl = TelSetVariableNodeHandler.Resolve(target.BaseUrl, ctx);
                var resolvedPath    = TelSetVariableNodeHandler.Resolve(target.Path, ctx);
                var resolvedUrl     = resolvedBaseUrl.TrimEnd('/') + "/" + resolvedPath.TrimStart('/');
                var resolvedBody    = target.RequestBodyTemplate is { } bodyTemplate
                    ? TelSetVariableNodeHandler.Resolve(bodyTemplate, ctx)
                    : null;
                var headers     = ResolveHeaders(target.Headers, ctx);
                var queryParams = ResolveQueryParams(target.QueryParams, ctx);
                var hmacPayload = ResolveHmacPayload(target.AuthConfig, ctx);

                Func<string, CancellationToken, Task<string?>> getCredential = scope == "portal"
                    ? portalCredentials.GetAsync
                    : (key, c) => tenantCredentials.GetForTenantAsync(ctx.TenantSubdomain, key, c);

                var timeoutOverride = node["timeoutSeconds"] is JsonValue tv && tv.TryGetValue<int>(out var overrideSeconds) && overrideSeconds > 0
                    ? overrideSeconds
                    : (int?)null;

                result = await executor.ExecuteAsync(new ApiDefinitionExecutionRequest(
                    HttpMethod: target.HttpMethod,
                    Url: resolvedUrl,
                    Headers: headers,
                    QueryParams: queryParams,
                    Body: resolvedBody,
                    AuthConfigJson: target.AuthConfig,
                    TimeoutSeconds: timeoutOverride ?? target.TimeoutSeconds,
                    GetCredential: getCredential,
                    DefinitionId: target.DefinitionId,
                    AllowRetryOnAmbiguousFailure: target.IsRetrySafe,
                    RateLimitPerMinute: target.RateLimitPerMinute,
                    HmacPayload: hmacPayload), ct);

                transitionKey = result.TimedOut ? "timeout" : (!result.Success ? "error" : "success");
            }
        }

        if (!string.IsNullOrEmpty(outputVariable))
        {
            // Masked BEFORE it ever reaches flow variables — see the identical comment in
            // ApiCallNodeHandler (the CRM-engine sibling this was ported from).
            var maskedResult = ResponseFieldMasker.Mask(result, targetSensitiveFields);
            ctx.Vars[outputVariable] = ApiResponseWrapper.BuildJson(maskedResult);
            foreach (var (key, value) in ApiResponseWrapper.BuildFlat(maskedResult))
                ctx.Vars[$"{outputVariable}.{key}"] = value;
        }

        var nextNodeId = node["transitions"]?[transitionKey]?.GetValue<string>()
                       ?? node["transitions"]?["default"]?.GetValue<string>();

        return new TelephonyNodeResult(nextNodeId, transitionKey);
    }

    private async Task<CallTarget?> LoadTenantAsync(string schemaName, Guid endpointId, CancellationToken ct)
    {
        await using var db = tenantDbFactory.Create(schemaName);
        var endpoint = await db.TenantApiEndpoints.FirstOrDefaultAsync(e => e.Id == endpointId, ct);
        if (endpoint is null) return null;
        var def = await db.TenantApiDefinitions.FirstOrDefaultAsync(d => d.Id == endpoint.DefinitionId, ct);
        if (def is null) return null;
        return new CallTarget(
            def.Id, endpoint.HttpMethod ?? def.HttpMethod, def.BaseUrl, endpoint.Path, endpoint.Headers, endpoint.QueryParams,
            endpoint.RequestBodyTemplate, def.AuthConfig, def.TimeoutSeconds, def.IsActive && endpoint.IsActive, endpoint.IsRetrySafe,
            def.RateLimitPerMinute, endpoint.SensitiveResponseFields);
    }

    private async Task<CallTarget?> LoadPortalAsync(Guid endpointId, CancellationToken ct)
    {
        var endpoint = await portalDb.PortalApiEndpoints.FirstOrDefaultAsync(e => e.Id == endpointId, ct);
        if (endpoint is null) return null;
        var def = await portalDb.PortalApiDefinitions.FirstOrDefaultAsync(d => d.Id == endpoint.DefinitionId, ct);
        if (def is null) return null;
        return new CallTarget(
            def.Id, endpoint.HttpMethod ?? def.HttpMethod, def.BaseUrl, endpoint.Path, endpoint.Headers, endpoint.QueryParams,
            endpoint.RequestBodyTemplate, def.AuthConfig, def.TimeoutSeconds, def.IsActive && endpoint.IsActive, endpoint.IsRetrySafe,
            def.RateLimitPerMinute, endpoint.SensitiveResponseFields);
    }

    /// <summary>Telephony-engine twin of ApiCallNodeHandler.ResolveHmacPayload — extracts the
    /// hmac auth type's optional payloadTemplate and resolves it through the same variable
    /// resolver as the request body/headers/query params.</summary>
    private static string? ResolveHmacPayload(string authConfigJson, TelephonyFlowContext ctx)
    {
        try
        {
            var root = JsonNode.Parse(authConfigJson)?.AsObject();
            if (root is null || root["type"]?.GetValue<string>() != "hmac") return null;
            var template = root["payloadTemplate"]?.GetValue<string>();
            return string.IsNullOrEmpty(template) ? null : TelSetVariableNodeHandler.Resolve(template, ctx);
        }
        catch { return null; }
    }

    private static Dictionary<string, string> ResolveHeaders(string json, TelephonyFlowContext ctx)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            var obj = JsonNode.Parse(json)?.AsObject();
            if (obj is null) return result;
            foreach (var (key, value) in obj)
                result[key] = TelSetVariableNodeHandler.Resolve(value?.GetValue<string>() ?? string.Empty, ctx);
        }
        catch { /* malformed JSON on the endpoint — treat as no headers */ }
        return result;
    }

    /// <summary>
    /// Same "_skipIfEmpty" convention as the Admin/Portal API Definition endpoint test runner
    /// (ApiEndpointTestHelper): a query param key listed in "_skipIfEmpty" is omitted from the
    /// request entirely if its resolved value is empty, instead of being sent as "".
    /// </summary>
    private static Dictionary<string, string> ResolveQueryParams(string json, TelephonyFlowContext ctx)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            var obj = JsonNode.Parse(json)?.AsObject();
            if (obj is null) return result;

            var skipIfEmpty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (obj["_skipIfEmpty"] is JsonArray skipArr)
                foreach (var item in skipArr)
                    if (item?.GetValue<string>() is { } s) skipIfEmpty.Add(s);

            foreach (var (key, value) in obj)
            {
                if (key.StartsWith('_')) continue;
                var resolved = TelSetVariableNodeHandler.Resolve(value?.GetValue<string>() ?? string.Empty, ctx);
                if (skipIfEmpty.Contains(key) && string.IsNullOrEmpty(resolved)) continue;
                result[key] = resolved;
            }
        }
        catch { /* malformed JSON on the endpoint — treat as no query params */ }
        return result;
    }
}
