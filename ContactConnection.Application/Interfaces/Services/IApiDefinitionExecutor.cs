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
    Func<string, CancellationToken, Task<string?>> GetCredential);

public record ApiDefinitionExecutionResult(
    bool Success,
    int? StatusCode,
    string? StatusMessage,
    Dictionary<string, string> ResponseHeaders,
    string? ResponseBody,
    bool TimedOut,
    string? Error);
