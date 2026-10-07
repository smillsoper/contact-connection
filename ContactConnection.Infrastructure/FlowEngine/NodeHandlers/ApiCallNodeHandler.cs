using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.Common;
using Microsoft.Extensions.Logging;

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
///   "oncePerCall": true,   // optional — see below
///   "releasesCardData": true, // optional — the order submission: wipe the captured card on success
///   "transitions": { "success": "node_x", "error": "node_y", "timeout": "node_z" }
/// }
///
/// Endpoint body modes: "simple" resolves {{namespace.field}} tags; "liquid" renders the body as a
/// Liquid template against IApiTemplateModelBuilder's model (flow vars, call record + addresses,
/// cart + fees, payment). A Liquid body declared as JSON (Content-Type header containing "json",
/// or no Content-Type) must render to valid JSON, or the call fails before anything is sent.
///
/// After a 2xx response the endpoint's SuccessCriteria (if any) decide success from the body —
/// e.g. an order API returning 200 with {"success": false}. See ResponseSuccessEvaluator.
///
/// oncePerCall: once this endpoint has succeeded on this call, later executions of the node replay
/// the stored result (transitioning "success" with the same output variables) instead of calling
/// the API again — the submit-once guard for order submissions. Failures are never stored.
/// </summary>
public class ApiCallNodeHandler(
    IVariableResolver resolver,
    ITenantApiDefinitionRepository tenantDefinitions,
    IPortalApiDefinitionRepository portalDefinitions,
    ITenantApiEndpointRepository tenantEndpoints,
    IPortalApiEndpointRepository portalEndpoints,
    ITenantCredentialStore tenantCredentials,
    IPortalCredentialStore portalCredentials,
    IApiDefinitionExecutor executor,
    ILiquidTemplateRenderer liquid,
    IApiTemplateModelBuilder templateModel,
    IApiResponseCacheStore responseCache,
    ICardDataRetentionService cardRetention,
    ICommissionService? commissions = null,
    ILogger<ApiCallNodeHandler>? logger = null)
    : NodeHandlerBase(resolver), INodeHandler
{
    public string NodeType => "api_call";

    private record CallTarget(
        Guid DefinitionId, string HttpMethod, string BaseUrl, string Path, string Headers, string QueryParams,
        string? RequestBodyTemplate, string AuthConfig, int TimeoutSeconds, bool IsActive, bool IsRetrySafe,
        int? RateLimitPerMinute, string SensitiveResponseFields, string BodyTemplateType, string SuccessCriteria,
        string? EndpointName = null, bool IsClientApi = false, string? TrainingResponse = null,
        string? SandboxBaseUrl = null, string? SandboxPath = null, string? SandboxTokenUrl = null, bool TrainingUsesSandbox = false,
        bool Sandbox = false, bool DesignerSandboxUsesSandbox = true)
    {
        /// <summary>The same call, pointed at the definition's sandbox environment (S179).</summary>
        public CallTarget ForSandbox() => this with
        {
            BaseUrl = SandboxBaseUrl!,
            Path = string.IsNullOrWhiteSpace(SandboxPath) ? Path : SandboxPath,
            AuthConfig = SandboxEnvironment.AuthConfig(AuthConfig, SandboxTokenUrl),
            DefinitionId = SandboxEnvironment.DefinitionId(DefinitionId),
            Sandbox = true,
        };
    }

    private enum RunEnvironment { Production, Sandbox, Simulated }

    /// <summary>
    /// Launch modes (S179). Live calls and platform APIs (address lookups, speech) always run for real, and so does a
    /// designer sandbox run that chose production credentials. Otherwise a practice run reaches the client's API only
    /// through its sandbox environment, when the definition has one and allows it for that mode (one checkbox each for
    /// designer sandbox and training runs), and is simulated (Training response) everywhere else.
    /// </summary>
    private static RunEnvironment EnvironmentFor(CallTarget target, FlowExecutionContext ctx)
    {
        // S181: a designer sandbox run can choose each API's environment at launch. Live calls never get here with a
        // choice (the record only carries choices on sandbox runs).
        if (target.IsClientApi && ctx.CallRecord.GetValueOrDefault($"_env:api:{target.DefinitionId}") is { } choice)
            return choice switch
            {
                "production" => RunEnvironment.Production,
                "sandbox" => string.IsNullOrWhiteSpace(target.SandboxBaseUrl) ? RunEnvironment.Simulated : RunEnvironment.Sandbox,
                _ => RunEnvironment.Simulated,
            };
        if (!target.IsClientApi || !SimulatesClientApis(ctx)) return RunEnvironment.Production;
        if (string.IsNullOrWhiteSpace(target.SandboxBaseUrl)) return RunEnvironment.Simulated;
        var designerSandbox = ctx.CallRecord.GetValueOrDefault("run_mode") == "sandbox";
        var allowed = designerSandbox ? target.DesignerSandboxUsesSandbox : target.TrainingUsesSandbox;
        return allowed ? RunEnvironment.Sandbox : RunEnvironment.Simulated;
    }

    public async Task<NodeResult> ExecuteAsync(
        JsonObject node, FlowExecutionContext ctx,
        string? agentInput, string agentTransition, CancellationToken ct = default)
    {
        var outputVariable = Str(node, "outputVariable")?.Trim();
        var scope = Str(node, "apiDefinitionScope") ?? "tenant";
        var endpointIdStr = Str(node, "apiEndpointId");

        ApiDefinitionExecutionResult result;
        string transitionKey;
        var simulated = false;
        var sandboxed = false;
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
            else if (node["oncePerCall"]?.GetValue<bool>() == true
                     && await responseCache.GetAsync(ctx.CallRecordId, OnceKey(scope, endpointId), ct) is { } cachedJson
                     && JsonSerializer.Deserialize<ApiDefinitionExecutionResult>(cachedJson) is { } cached)
            {
                // Already succeeded on this call — replay instead of submitting again.
                result = cached;
                transitionKey = "success";
            }
            else
            {
                // Launch modes (S179): production, the client's sandbox environment, or simulated — see EnvironmentFor.
                // A simulated call still builds its body, so template errors show up in practice too.
                var environment = EnvironmentFor(target, ctx);
                if (environment == RunEnvironment.Sandbox) target = target.ForSandbox();
                var (request, bodyError) = await BuildRequestAsync(target, node, ctx, scope, ct);
                simulated = bodyError is null && environment == RunEnvironment.Simulated;
                sandboxed = target.Sandbox;
                var missingSandboxCredential = bodyError is null && target.Sandbox
                    ? await SandboxEnvironment.FirstMissingCredentialAsync(target.AuthConfig, tenantCredentials.GetAsync, ct)
                    : null;
                result = bodyError is not null
                    // Never send a body the template couldn't produce correctly.
                    ? new ApiDefinitionExecutionResult(false, null, null, new(), null, false, bodyError)
                    : missingSandboxCredential is not null
                        // Never let a sandbox call go out on production credentials (or none).
                        ? new ApiDefinitionExecutionResult(false, null, null, new(), null, false,
                            SandboxEnvironment.MissingCredentialError(missingSandboxCredential))
                        : simulated
                            ? SimulatedResult(target.TrainingResponse)
                            : await executor.ExecuteAsync(request, ct);

                result = ResponseSuccessEvaluator.Apply(result, target.SuccessCriteria);
                transitionKey = result.TimedOut ? "timeout" : (!result.Success ? "error" : "success");

                if (result.Success && node["oncePerCall"]?.GetValue<bool>() == true)
                    await responseCache.SetAsync(ctx.CallRecordId, OnceKey(scope, endpointId),
                        JsonSerializer.Serialize(ResponseFieldMasker.Mask(result, targetSensitiveFields)), ct);
            }
        }

        // The order submission: once it has gone through, the captured card has served its purpose
        // (CardDataRetentionMode.UntilOrderSubmitted keeps it until exactly here). A replayed
        // once-per-call success counts too.
        if (transitionKey == "success" && node["releasesCardData"]?.GetValue<bool>() == true)
        {
            await cardRetention.ReleaseAfterOrderSubmittedAsync(ctx.CallRecordId, ct);

            // The order is placed: stamp it and record the agent's commission (S171). Never fails the flow.
            if (commissions is not null)
            {
                try { await commissions.OrderSubmittedAsync(ctx.CallRecordId, ct, ctx.InteractionId); }
                catch (Exception ex) { logger?.LogWarning(ex, "Commission recording failed for call {CallRecordId}", ctx.CallRecordId); }
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
        AppendHistory(ctx, node, input: simulated ? SimulatedHistoryNote : sandboxed ? SandboxHistoryNote : null, transition: next);

        var state = BuildState(ctx, node, resolvedContent: string.Empty);
        return new NodeResult(state, next);
    }

    private static string OnceKey(string scope, Guid endpointId) => $"once:{scope}:{endpointId}";

    internal const string SimulatedHistoryNote = "Simulated (practice run): the client's API was not called";
    internal const string SandboxHistoryNote = "Sent to the client's sandbox environment (practice run)";
    internal const string GenericSimulatedBody = "{\"success\":true,\"simulated\":true}";

    /// <summary>A practice run on the sandbox credential set (training always is) simulates the client's APIs.</summary>
    internal static bool SimulatesClientApis(FlowExecutionContext ctx) =>
        ctx.CallRecord.GetValueOrDefault("run_mode") is { Length: > 0 } rm && rm != "production"
        && ctx.CallRecord.GetValueOrDefault("credential_set") != "production";

    private static ApiDefinitionExecutionResult SimulatedResult(string? trainingResponse) =>
        new(true, 200, trainingResponse is null ? "OK (simulated)" : "OK (simulated: training response)", new(),
            trainingResponse ?? GenericSimulatedBody, false, null);

    /// <summary>Resolves everything the request needs — URL, headers, query, body (simple tags or Liquid)
    /// — exactly as sent. Shared by ExecuteAsync and PreviewAsync so a preview can never differ from the
    /// real request. A non-null BodyError means the body couldn't be produced and nothing may be sent.</summary>
    private async Task<(ApiDefinitionExecutionRequest Request, string? BodyError)> BuildRequestAsync(
        CallTarget target, JsonObject node, FlowExecutionContext ctx, string scope, CancellationToken ct)
    {
        var varCtx = ctx.ToVariableContext();
        var resolvedBaseUrl = Resolver.Resolve(target.BaseUrl, varCtx);
        var resolvedPath    = Resolver.Resolve(target.Path, varCtx);
        var resolvedUrl     = resolvedBaseUrl.TrimEnd('/') + "/" + resolvedPath.TrimStart('/');
        var headers         = ResolveHeaders(target.Headers, varCtx);
        string? resolvedBody = null;
        string? bodyError = null;
        if (target.RequestBodyTemplate is { } bodyTemplate)
        {
            if (target.BodyTemplateType == BodyTemplateType.Liquid)
            {
                var rendered = await liquid.RenderAsync(bodyTemplate, await templateModel.BuildAsync(ctx, ct), ct);
                resolvedBody = rendered.Output;
                bodyError = rendered.Success ? LiquidJsonCheck(rendered.Output, headers) : rendered.Error;
            }
            else
            {
                resolvedBody = Resolver.Resolve(bodyTemplate, varCtx);
            }
        }

        Func<string, CancellationToken, Task<string?>> getCredential = scope == "portal"
            ? portalCredentials.GetAsync
            : target.Sandbox
                ? SandboxEnvironment.Credentials(tenantCredentials.GetAsync)   // {key}.sandbox — never the production value
                : tenantCredentials.GetAsync;
        var timeoutOverride = node["timeoutSeconds"] is JsonValue tv && tv.TryGetValue<int>(out var overrideSeconds) && overrideSeconds > 0
            ? overrideSeconds
            : (int?)null;

        return (new ApiDefinitionExecutionRequest(
            HttpMethod: target.HttpMethod,
            Url: resolvedUrl,
            Headers: headers,
            QueryParams: ResolveQueryParams(target.QueryParams, varCtx),
            Body: resolvedBody,
            AuthConfigJson: target.AuthConfig,
            TimeoutSeconds: timeoutOverride ?? target.TimeoutSeconds,
            GetCredential: getCredential,
            DefinitionId: target.DefinitionId,
            AllowRetryOnAmbiguousFailure: target.IsRetrySafe,
            RateLimitPerMinute: target.RateLimitPerMinute,
            HmacPayload: ResolveHmacPayload(target.AuthConfig, varCtx)), bodyError);
    }

    /// <summary>Renders the node's request against this context without sending it (S169).</summary>
    public async Task<ApiRequestPreview> PreviewAsync(JsonObject node, FlowExecutionContext ctx, CancellationToken ct = default)
    {
        var scope = Str(node, "apiDefinitionScope") ?? "tenant";
        if (!Guid.TryParse(Str(node, "apiEndpointId"), out var endpointId))
            return ApiRequestPreviewer.Failed("This API Call node has no API endpoint selected.");
        var target = scope == "portal" ? await LoadPortalAsync(endpointId, ct) : await LoadTenantAsync(endpointId, ct);
        if (target is null) return ApiRequestPreviewer.Failed("API endpoint not found.");
        if (EnvironmentFor(target, ctx) == RunEnvironment.Sandbox) target = target.ForSandbox();

        var (request, bodyError) = await BuildRequestAsync(target, node, ctx, scope, ct);
        return ApiRequestPreviewer.From(target.EndpointName, request, target.BodyTemplateType, bodyError);
    }

    /// <summary>A Liquid body meant to be JSON must parse as JSON — catching a template bug (a
    /// stray comma, an unquoted string) with a clear message instead of a vendor's 400.</summary>
    public static string? LiquidJsonCheck(string? body, Dictionary<string, string> headers)
    {
        var contentType = headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
        if (contentType is not null && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { using var _ = JsonDocument.Parse(body); return null; }
        catch (JsonException ex) { return $"Liquid template produced invalid JSON: {ex.Message}"; }
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
            def.RateLimitPerMinute, endpoint.SensitiveResponseFields, endpoint.BodyTemplateType, endpoint.SuccessCriteria,
            $"{def.Name} → {endpoint.Name}", IsClientApi: true, endpoint.TrainingResponse,
            def.SandboxBaseUrl, endpoint.SandboxPath, def.SandboxTokenUrl, def.TrainingUsesSandbox,
            DesignerSandboxUsesSandbox: def.DesignerSandboxUsesSandbox);
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
            def.RateLimitPerMinute, endpoint.SensitiveResponseFields, endpoint.BodyTemplateType, endpoint.SuccessCriteria,
            $"{def.Name} → {endpoint.Name}");
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
