using System.Text.Json.Nodes;
using System.Web;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// Turns a built (not sent) API request into a display/trace-safe preview (S169): the full URL with its
/// query string, headers with auth-looking values masked, the body, and the auth type by name only —
/// credentials are applied at send time and never appear here.
/// </summary>
public static class ApiRequestPreviewer
{
    private const string Masked = "••••••••";

    private static readonly string[] SecretHeaderHints =
        ["authorization", "api-key", "apikey", "x-api-key", "token", "secret", "password", "signature", "cookie"];

    public static ApiRequestPreview Failed(string error) => new(null, null, null, new Dictionary<string, string>(), null, null, null, error);

    public static ApiRequestPreview From(string? endpointName, ApiDefinitionExecutionRequest request, string? bodyTemplateType, string? bodyError)
        => new(
            endpointName,
            request.HttpMethod.ToUpperInvariant(),
            UrlWithQuery(request.Url, request.QueryParams),
            MaskHeaders(request.Headers),
            request.Body,
            bodyTemplateType,
            AuthType(request.AuthConfigJson),
            bodyError);

    public static Dictionary<string, string> MaskHeaders(IReadOnlyDictionary<string, string> headers)
        => headers.ToDictionary(h => h.Key,
            h => SecretHeaderHints.Any(hint => h.Key.Contains(hint, StringComparison.OrdinalIgnoreCase)) ? Masked : h.Value);

    public static string UrlWithQuery(string url, IReadOnlyDictionary<string, string> query)
    {
        if (query.Count == 0) return url;
        try
        {
            var builder = new UriBuilder(url);
            var qs = HttpUtility.ParseQueryString(builder.Query);
            foreach (var (k, v) in query) qs[k] = v;
            builder.Query = qs.ToString();
            return builder.Uri.ToString();
        }
        catch (UriFormatException) { return url; }
    }

    public static string? AuthType(string authConfigJson)
    {
        try { return JsonNode.Parse(authConfigJson)?["type"]?.GetValue<string>(); }
        catch { return null; }
    }
}
