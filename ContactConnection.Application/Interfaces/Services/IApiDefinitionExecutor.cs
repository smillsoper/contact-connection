namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Executes a fully-resolved HTTP call against a "general" API Definition (tenant or portal
/// scoped) and returns a uniform result. Callers (flow engine node handlers) are responsible
/// for resolving {{...}} template tags in Url/Headers/QueryParams/Body before calling this —
/// this executor performs no templating of its own, only HTTP mechanics and auth.
/// </summary>
public interface IApiDefinitionExecutor
{
    Task<ApiDefinitionExecutionResult> ExecuteAsync(
        ApiDefinitionExecutionRequest request, CancellationToken ct = default);
}

public record ApiDefinitionExecutionRequest(
    string HttpMethod,
    string Url,
    Dictionary<string, string> Headers,
    Dictionary<string, string> QueryParams,
    string? Body,
    string AuthConfigJson,
    int TimeoutSeconds,
    Func<string, CancellationToken, Task<string?>> GetCredential,
    /// <summary>Identifies the vendor for circuit-breaker keying — see IVendorResilienceExecutor.
    /// Guid.Empty is accepted (falls back to a single shared circuit) for any caller that hasn't
    /// been updated to pass a real definition id yet.</summary>
    Guid DefinitionId = default,
    /// <summary>True when a timeout/5xx on this specific call is safe to automatically retry —
    /// always true for GET/HEAD/PUT/DELETE; for POST/PATCH, only when the endpoint's own
    /// IsRetrySafe flag is set. A pure connection-level failure (request never reached the
    /// vendor) is retried regardless of this flag. See API_HARDENING_CHECKLIST.md Tier 1.</summary>
    bool AllowRetryOnAmbiguousFailure = false,
    /// <summary>The definition's own configured outbound rate limit (requests/minute), or null
    /// for unlimited. See API_HARDENING_CHECKLIST.md Tier 2.</summary>
    int? RateLimitPerMinute = null,
    /// <summary>Only meaningful when AuthConfigJson's type is "hmac". The already-resolved string
    /// to sign — the caller resolves the auth config's payloadTemplate (if any) through its own
    /// variable-resolution mechanism before constructing this request, exactly like Url/Headers/
    /// Body are resolved, since this executor does no templating of its own. Null means "no
    /// payloadTemplate configured" — the hmac case signs Body instead.</summary>
    string? HmacPayload = null);

public record ApiDefinitionExecutionResult(
    bool Success,
    int? StatusCode,
    string? StatusMessage,
    Dictionary<string, string> ResponseHeaders,
    string? ResponseBody,
    bool TimedOut,
    string? Error);
