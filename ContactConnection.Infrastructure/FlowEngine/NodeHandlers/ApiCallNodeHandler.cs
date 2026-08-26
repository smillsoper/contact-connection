using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Common;

namespace ContactConnection.Infrastructure.FlowEngine.NodeHandlers;

/// <summary>
/// Handles "api_call" nodes — invokes a saved endpoint of a "general"-category API Definition
/// (tenant or portal scoped). A general definition is a connection (base URL, auth, timeout);
/// its endpoints are the individual callable operations. Wraps the result as { success,
/// status_code, status_message, response_headers, response, error, timed_out }, and stores it
/// flattened into flow variables under outputVariable so any piece can be referenced via
/// {{flow.outputVariable.response.field}}.
///
/// Node schema:
/// {
///   "type": "api_call",
///   "label": "Get Stats",
///   "apiEndpointId": "guid",
///   "apiDefinitionScope": "tenant" | "portal",
///   "outputVariable": "statsApi",
///   "timeoutSeconds": 30,
///   "transitions": { "success": "node_x", "error": "node_y", "timeout": "node_z" }
/// }
/// </summary>
public class ApiCallNodeHandler(
    IVariableResolver resolver,
    ITenantApiDefinitionRepository tenantDefinitions,
    IPortalApiDefinitionRepository portalDefinitions,
    ITenantApiEndpointRepository tenantEndpoints,
    IPortalApiEndpointRepository portalEndpoints,
    ITenantCredentialStore tenantCredentials,
    IPortalCredentialStore portalCredentials,
    IApiDefinitionExecutor executor)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "api_call";

    private record CallTarget(
        Guid DefinitionId, string HttpMethod, string BaseUrl, string Path, string Headers, string QueryParams,
        string? RequestBodyTemplate, string AuthConfig, int TimeoutSeconds, bool IsActive, bool IsRetrySafe,
        int? RateLimitPerMinute, string SensitiveResponseFields);

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var varCtx = ctx.ToVariableContext();
        var outputVariable = Str(node, "outputVariable")?.Trim();
        var scope = Str(node, "apiDefinitionScope") ?? "tenant";
        var endpointIdStr = Str(node, "apiEndpointId");

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
                : await LoadTenantAsync(endpointId, ct);
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
                var resolvedBaseUrl = Resolver.Resolve(target.BaseUrl, varCtx);
                var resolvedPath    = Resolver.Resolve(target.Path, varCtx);
                var resolvedUrl     = resolvedBaseUrl.TrimEnd('/') + "/" + resolvedPath.TrimStart('/');
                var resolvedBody    = target.RequestBodyTemplate is { } bodyTemplate
                    ? Resolver.Resolve(bodyTemplate, varCtx)
                    : null;
                var headers     = ResolveHeaders(target.Headers, varCtx);
                var queryParams = ResolveQueryParams(target.QueryParams, varCtx);
                var hmacPayload = ResolveHmacPayload(target.AuthConfig, varCtx);

                Func<string, CancellationToken, Task<string?>> getCredential = scope == "portal"
                    ? portalCredentials.GetAsync
                    : tenantCredentials.GetAsync;

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
            // Masked BEFORE it ever reaches flow variables — this is what actually lands in
            // flow_sessions.variable_store, so masking has to happen here, not just at display
            // time. targetSensitiveFields is empty ("[]") for every early-exit branch above
            // (endpoint not found/inactive), where ResponseFieldMasker.Mask is a no-op anyway
            // since those results never have a ResponseBody.
            var maskedResult = ResponseFieldMasker.Mask(result, targetSensitiveFields);
            ctx.FlowVars[outputVariable] = ApiResponseWrapper.BuildJson(maskedResult);
            foreach (var (key, value) in ApiResponseWrapper.BuildFlat(maskedResult))
                ctx.FlowVars[$"{outputVariable}.{key}"] = value;
        }

        var next = Transition(node, transitionKey) ?? Transition(node, "default");
        AppendHistory(ctx, node, input: null, transition: next);

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }

    private async Task<CallTarget?> LoadTenantAsync(Guid endpointId, CancellationToken ct)
    {
        var endpoint = await tenantEndpoints.GetByIdAsync(endpointId, ct);
        if (endpoint is null) return null;
        var def = await tenantDefinitions.GetByIdAsync(endpoint.DefinitionId, ct);
        if (def is null) return null;
        return new CallTarget(
            def.Id, endpoint.HttpMethod ?? def.HttpMethod, def.BaseUrl, endpoint.Path, endpoint.Headers, endpoint.QueryParams,
            endpoint.RequestBodyTemplate, def.AuthConfig, def.TimeoutSeconds, def.IsActive && endpoint.IsActive, endpoint.IsRetrySafe,
            def.RateLimitPerMinute, endpoint.SensitiveResponseFields);
    }

    private async Task<CallTarget?> LoadPortalAsync(Guid endpointId, CancellationToken ct)
    {
        var endpoint = await portalEndpoints.GetByIdAsync(endpointId, ct);
        if (endpoint is null) return null;
        var def = await portalDefinitions.GetByIdAsync(endpoint.DefinitionId, ct);
        if (def is null) return null;
        return new CallTarget(
            def.Id, endpoint.HttpMethod ?? def.HttpMethod, def.BaseUrl, endpoint.Path, endpoint.Headers, endpoint.QueryParams,
            endpoint.RequestBodyTemplate, def.AuthConfig, def.TimeoutSeconds, def.IsActive && endpoint.IsActive, endpoint.IsRetrySafe,
            def.RateLimitPerMinute, endpoint.SensitiveResponseFields);
    }

    /// <summary>Extracts the hmac auth type's optional payloadTemplate (if any) and resolves it
    /// through the same variable resolver as the request body/headers/query params — so the
    /// signed string can pull in fields the vendor requires even when they aren't part of the
    /// outgoing body. Returns null when the auth type isn't "hmac" or no template is configured,
    /// meaning ApiDefinitionExecutor falls back to signing the actual request body.</summary>
    private string? ResolveHmacPayload(string authConfigJson, VariableContext varCtx)
    {
        try
        {
            var root = JsonNode.Parse(authConfigJson)?.AsObject();
            if (root is null || root["type"]?.GetValue<string>() != "hmac") return null;
            var template = root["payloadTemplate"]?.GetValue<string>();
            return string.IsNullOrEmpty(template) ? null : Resolver.Resolve(template, varCtx);
        }
        catch { return null; } // malformed auth config JSON — same "treat as absent" tolerance as headers/query params
    }

    private Dictionary<string, string> ResolveHeaders(string json, VariableContext varCtx)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            var obj = JsonNode.Parse(json)?.AsObject();
            if (obj is null) return result;
            foreach (var (key, value) in obj)
                result[key] = Resolver.Resolve(value?.GetValue<string>() ?? string.Empty, varCtx);
        }
        catch { /* malformed JSON on the endpoint — treat as no headers */ }
        return result;
    }

    /// <summary>
    /// Same "_skipIfEmpty" convention as the Admin/Portal API Definition endpoint test runner
    /// (ApiEndpointTestHelper): a query param key listed in "_skipIfEmpty" is omitted from the
    /// request entirely if its resolved value is empty, instead of being sent as "".
    /// </summary>
    private Dictionary<string, string> ResolveQueryParams(string json, VariableContext varCtx)
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
                var resolved = Resolver.Resolve(value?.GetValue<string>() ?? string.Empty, varCtx);
                if (skipIfEmpty.Contains(key) && string.IsNullOrEmpty(resolved)) continue;
                result[key] = resolved;
            }
        }
        catch { /* malformed JSON on the endpoint — treat as no query params */ }
        return result;
    }
}
