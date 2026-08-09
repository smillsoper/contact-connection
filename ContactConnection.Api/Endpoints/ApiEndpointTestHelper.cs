using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.ApiExecution;

namespace ContactConnection.Api.Endpoints;

public record RunEndpointTestRequest(
    string Path,
    string? HttpMethod,
    string? QueryParams,
    string? Headers,
    string? RequestBodyTemplate,
    string Namespace,
    Dictionary<string, string> TestData);

public record RunEndpointTestResponse(
    bool Success,
    int? StatusCode,
    string? Body,
    Dictionary<string, string>? ResponseHeaders,
    string? ResolvedUrl,
    string? Error);

internal static class ApiEndpointTestHelper
{
    public static string SubstituteVars(string? template, string ns, Dictionary<string, string> data)
    {
        if (string.IsNullOrEmpty(template)) return template ?? "";
        // Unmatched variables resolve to "" — an absent field means the agent left it blank,
        // not that the template is wrong (avoids sending literal {{address.address2}} to the API).
        return Regex.Replace(
            template,
            @"\{\{" + Regex.Escape(ns) + @"\.(\w+)\}\}",
            m => data.TryGetValue(m.Groups[1].Value, out var val) ? val : "");
    }

    /// <summary>Runs the test and returns the raw response (not wrapped in IResult).
    /// tokenCache is deliberately optional and defaults to null: the admin/portal "Test" button
    /// call sites omit it so a test click always reflects live config, never a cached oauth2
    /// token; FlowSessionsEndpoints' live address validation/ZIP/autocomplete resolution passes
    /// the real cache since that traffic runs on every call, not once per manual click.</summary>
    public static async Task<RunEndpointTestResponse> RunTestAsync(
        string baseUrl,
        string authConfigJson,
        RunEndpointTestRequest req,
        Func<string, CancellationToken, Task<string?>> getCredential,
        IHttpClientFactory httpFactory,
        CancellationToken ct,
        IOAuth2TokenCache? tokenCache = null)
    {
        try
        {
            var ns   = req.Namespace;
            var data = req.TestData ?? new();

            var method = req.HttpMethod?.ToUpperInvariant() switch
            {
                "POST"   => HttpMethod.Post,
                "PUT"    => HttpMethod.Put,
                "PATCH"  => HttpMethod.Patch,
                "DELETE" => HttpMethod.Delete,
                _        => HttpMethod.Get,
            };

            var resolvedPath = SubstituteVars(req.Path, ns, data);
            var uriBuilder   = new UriBuilder(baseUrl.TrimEnd('/') + "/" + resolvedPath.TrimStart('/'));

            if (!string.IsNullOrEmpty(req.QueryParams))
            {
                try
                {
                    var qpRoot = JsonDocument.Parse(req.QueryParams).RootElement;
                    var skipIfEmpty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (qpRoot.TryGetProperty("_skipIfEmpty", out var skipEl) && skipEl.ValueKind == JsonValueKind.Array)
                        foreach (var item in skipEl.EnumerateArray())
                            if (item.GetString() is string s) skipIfEmpty.Add(s);

                    var qs = HttpUtility.ParseQueryString(uriBuilder.Query);
                    foreach (var prop in qpRoot.EnumerateObject())
                    {
                        if (prop.Name.StartsWith('_')) continue;
                        var resolvedKey   = SubstituteVars(prop.Name, ns, data);
                        var resolvedValue = SubstituteVars(prop.Value.GetString() ?? "", ns, data);

                        if (string.IsNullOrWhiteSpace(resolvedKey)) continue;
                        if (skipIfEmpty.Contains(prop.Name) && string.IsNullOrEmpty(resolvedValue)) continue;

                        qs[resolvedKey] = resolvedValue;
                    }
                    uriBuilder.Query = qs.ToString();
                }
                catch { }
            }

            var request = new HttpRequestMessage(method, uriBuilder.Uri);

            if (!string.IsNullOrEmpty(req.Headers))
            {
                try
                {
                    var hdrDict = JsonSerializer.Deserialize<Dictionary<string, string>>(req.Headers);
                    if (hdrDict != null)
                        foreach (var (k, v) in hdrDict)
                            request.Headers.TryAddWithoutValidation(k, SubstituteVars(v, ns, data));
                }
                catch { }
            }

            await ApplyAuth(request, uriBuilder, authConfigJson, getCredential, httpFactory, ct, tokenCache);
            request.RequestUri = uriBuilder.Uri;

            if (!string.IsNullOrEmpty(req.RequestBodyTemplate))
            {
                var body = SubstituteVars(req.RequestBodyTemplate, ns, data);
                var contentType = request.Headers.TryGetValues("Content-Type", out var ctVals)
                    ? ctVals.FirstOrDefault() ?? "application/json"
                    : "application/json";
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
            }

            var http = httpFactory.CreateClient("FlowEngine");
            using var response = await http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            string prettyBody = responseBody;
            try
            {
                var el = JsonDocument.Parse(responseBody).RootElement;
                prettyBody = JsonSerializer.Serialize(el, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { }

            var responseHeaders = response.Headers
                .Concat(response.Content.Headers)
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value));

            return new RunEndpointTestResponse(
                Success: response.IsSuccessStatusCode,
                StatusCode: (int)response.StatusCode,
                Body: prettyBody,
                ResponseHeaders: responseHeaders,
                ResolvedUrl: uriBuilder.ToString(),
                Error: null);
        }
        catch (Exception ex)
        {
            return new RunEndpointTestResponse(
                Success: false,
                StatusCode: null,
                Body: null,
                ResponseHeaders: null,
                ResolvedUrl: null,
                Error: ex.Message);
        }
    }

    public static async Task<IResult> RunTest(
        string baseUrl,
        string authConfigJson,
        RunEndpointTestRequest req,
        Func<string, CancellationToken, Task<string?>> getCredential,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        // No tokenCache passed — the "Test" button always runs a live, uncached exchange.
        var result = await RunTestAsync(baseUrl, authConfigJson, req, getCredential, httpFactory, ct);
        return Results.Ok(result);
    }

    private static async Task ApplyAuth(
        HttpRequestMessage request,
        UriBuilder uriBuilder,
        string authConfigJson,
        Func<string, CancellationToken, Task<string?>> getCredential,
        IHttpClientFactory httpFactory,
        CancellationToken ct,
        IOAuth2TokenCache? tokenCache = null)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(authConfigJson).RootElement; }
        catch { return; }

        var type = root.TryGetProperty("type", out var tp) ? tp.GetString() ?? "none" : "none";

        switch (type)
        {
            case "api_key":
            {
                var credKey = Str(root, "credentialKey");
                var addTo   = Str(root, "addTo").Length > 0 ? Str(root, "addTo") : Str(root, "placement");
                var name    = Str(root, "headerName").Length > 0 ? Str(root, "headerName") : Str(root, "paramName");
                var val     = await getCredential(credKey, ct);
                if (val is null) break;
                if (addTo == "query")
                {
                    var qs = HttpUtility.ParseQueryString(uriBuilder.Query);
                    qs[name] = val;
                    uriBuilder.Query = qs.ToString();
                }
                else
                    request.Headers.TryAddWithoutValidation(name, val);
                break;
            }
            case "bearer":
            {
                var val = await getCredential(Str(root, "tokenKey"), ct);
                if (val is not null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", val);
                break;
            }
            case "basic":
            {
                var user = await getCredential(Str(root, "usernameKey"), ct);
                var pass = await getCredential(Str(root, "passwordKey"), ct);
                if (user is not null && pass is not null)
                {
                    var enc = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", enc);
                }
                break;
            }
            case "oauth2":
            {
                // Exchange client credentials for an access token, then apply as Bearer
                var tokenUrl       = Str(root, "tokenUrl");
                var method         = Str(root, "method").ToUpperInvariant();
                var placement      = Str(root, "credentialPlacement");
                var clientIdKey    = Str(root, "clientIdKey");
                var clientSecretKey = Str(root, "clientSecretKey");
                var contentType    = Str(root, "tokenRequestContentType");
                var bodyTemplate   = Str(root, "tokenRequestTemplate");
                var tokenField     = Str(root, "tokenField") is { Length: > 0 } tf ? tf : "access_token";
                var expiresInField = Str(root, "expiresInField") is { Length: > 0 } ef ? ef : "expires_in";
                var scopes         = Str(root, "scopes");

                if (string.IsNullOrWhiteSpace(tokenUrl)) break;

                var clientId     = await getCredential(clientIdKey, ct);
                var clientSecret = await getCredential(clientSecretKey, ct);
                if (clientId is null || clientSecret is null) break;

                string? cacheKey = null;
                if (tokenCache is not null)
                {
                    cacheKey = OAuth2CacheKey.Build(tokenUrl, method, placement, clientId, clientSecret, scopes, tokenField);
                    var cachedToken = await tokenCache.GetAsync(cacheKey, ct);
                    if (cachedToken is not null)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cachedToken);
                        break;
                    }
                }

                var tokenHttpMethod = method == "GET" ? HttpMethod.Get : HttpMethod.Post;
                var tokenRequest    = new HttpRequestMessage(tokenHttpMethod, tokenUrl);

                if (placement == "header")
                {
                    var enc = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                    tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", enc);
                    var form = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials") };
                    if (!string.IsNullOrEmpty(scopes)) form.Add(new("scope", scopes));
                    tokenRequest.Content = new FormUrlEncodedContent(form);
                }
                else
                {
                    var body = bodyTemplate
                        .Replace($"{{{{{clientIdKey}}}}}", clientId)
                        .Replace($"{{{{{clientSecretKey}}}}}", clientSecret);
                    var ct2 = string.IsNullOrEmpty(contentType) ? "application/json" : contentType;
                    tokenRequest.Content = new StringContent(body, Encoding.UTF8, ct2);
                }

                try
                {
                    var http = httpFactory.CreateClient("FlowEngine");
                    using var tokenResponse = await http.SendAsync(tokenRequest, ct);
                    var tokenBody = await tokenResponse.Content.ReadAsStringAsync(ct);
                    var tokenEl   = JsonDocument.Parse(tokenBody).RootElement;
                    if (tokenEl.TryGetProperty(tokenField, out var tokenProp))
                    {
                        var token = tokenProp.GetString() ?? tokenProp.ToString();
                        if (!string.IsNullOrEmpty(token))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                            if (tokenCache is not null && cacheKey is not null)
                                await tokenCache.SetAsync(cacheKey, token, OAuth2CacheKey.ComputeTtl(tokenEl, expiresInField), ct);
                        }
                    }
                }
                catch { /* token exchange failed — request proceeds without auth */ }
                break;
            }
            // hmac: requires request signing — not applied for inline test
        }
    }

    private static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";
}
