using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// Default IApiDefinitionExecutor — sends the already-resolved request, applies auth
/// (api_key/bearer/basic/oauth2) the same way ApiEndpointTestHelper does for the admin/portal
/// "test" endpoints, and normalizes the outcome (success/error/timeout) for flow engine use.
/// </summary>
public class ApiDefinitionExecutor(
    IHttpClientFactory httpClientFactory,
    IOAuth2TokenCache tokenCache,
    IVendorResilienceExecutor resilience) : IApiDefinitionExecutor
{
    public async Task<ApiDefinitionExecutionResult> ExecuteAsync(
        ApiDefinitionExecutionRequest request, CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var method = request.HttpMethod.ToUpperInvariant() switch
            {
                "POST"   => HttpMethod.Post,
                "PUT"    => HttpMethod.Put,
                "PATCH"  => HttpMethod.Patch,
                "DELETE" => HttpMethod.Delete,
                _        => HttpMethod.Get,
            };

            var uriBuilder = new UriBuilder(request.Url);
            if (request.QueryParams.Count > 0)
            {
                var qs = HttpUtility.ParseQueryString(uriBuilder.Query);
                foreach (var (key, value) in request.QueryParams)
                    qs[key] = value;
                uriBuilder.Query = qs.ToString();
            }

            var httpRequest = new HttpRequestMessage(method, uriBuilder.Uri);
            string? bodyContentType = null;
            foreach (var (key, value) in request.Headers)
            {
                // Content-Type is a content header, not a request header — HttpRequestHeaders
                // silently rejects it (TryAddWithoutValidation returns false, nothing is stored),
                // so it has to be captured here and applied to the StringContent below instead of
                // being read back off httpRequest.Headers, which would never find it.
                if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    bodyContentType = value;
                    continue;
                }
                httpRequest.Headers.TryAddWithoutValidation(key, value);
            }

            await ApplyAuthAsync(httpRequest, uriBuilder, request.AuthConfigJson, request.GetCredential, linkedCts.Token);
            httpRequest.RequestUri = uriBuilder.Uri;

            if (request.Body is not null)
                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, bodyContentType ?? "application/json");

            var http = httpClientFactory.CreateClient("FlowEngine");
            // GET/HEAD/PUT/DELETE are always safe to retry on an ambiguous failure by HTTP
            // semantics; for POST/PATCH, only the endpoint's own IsRetrySafe opt-in
            // (request.AllowRetryOnAmbiguousFailure) allows it.
            var alwaysRetrySafeMethod = method == HttpMethod.Get || method == HttpMethod.Head
                || method == HttpMethod.Put || method == HttpMethod.Delete;
            using var response = await resilience.SendAsync(
                request.DefinitionId, httpRequest,
                alwaysRetrySafeMethod || request.AllowRetryOnAmbiguousFailure,
                http, linkedCts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token);

            var responseHeaders = response.Headers
                .Concat(response.Content.Headers)
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value));

            return new ApiDefinitionExecutionResult(
                Success: response.IsSuccessStatusCode,
                StatusCode: (int)response.StatusCode,
                StatusMessage: response.ReasonPhrase,
                ResponseHeaders: responseHeaders,
                ResponseBody: responseBody,
                TimedOut: false,
                Error: null);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return new ApiDefinitionExecutionResult(
                Success: false, StatusCode: null, StatusMessage: null,
                ResponseHeaders: [], ResponseBody: null, TimedOut: true,
                Error: $"Request timed out after {request.TimeoutSeconds} second(s).");
        }
        catch (Exception ex)
        {
            return new ApiDefinitionExecutionResult(
                Success: false, StatusCode: null, StatusMessage: null,
                ResponseHeaders: [], ResponseBody: null, TimedOut: false,
                Error: ex.Message);
        }
    }

    // Ported from ContactConnection.Api/Endpoints/ApiEndpointTestHelper.ApplyAuth — same
    // auth-config JSON shape ("type": "none"|"api_key"|"bearer"|"basic"|"oauth2"), same
    // credential-key indirection via GetCredential. hmac (request signing) is intentionally
    // unsupported, matching the existing test-helper behavior.
    private async Task ApplyAuthAsync(
        HttpRequestMessage request,
        UriBuilder uriBuilder,
        string authConfigJson,
        Func<string, CancellationToken, Task<string?>> getCredential,
        CancellationToken ct)
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
                var tokenUrl        = Str(root, "tokenUrl");
                var method          = Str(root, "method").ToUpperInvariant();
                var placement       = Str(root, "credentialPlacement");
                var clientIdKey     = Str(root, "clientIdKey");
                var clientSecretKey = Str(root, "clientSecretKey");
                var contentType     = Str(root, "tokenRequestContentType");
                var bodyTemplate    = Str(root, "tokenRequestTemplate");
                var tokenField      = Str(root, "tokenField") is { Length: > 0 } tf ? tf : "access_token";
                var expiresInField  = Str(root, "expiresInField") is { Length: > 0 } ef ? ef : "expires_in";
                var scopes          = Str(root, "scopes");

                if (string.IsNullOrWhiteSpace(tokenUrl)) break;

                var clientId     = await getCredential(clientIdKey, ct);
                var clientSecret = await getCredential(clientSecretKey, ct);
                if (clientId is null || clientSecret is null) break;

                var cacheKey = OAuth2CacheKey.Build(tokenUrl, method, placement, clientId, clientSecret, scopes, tokenField);

                // GetOrCreateAsync (not a bare Get-then-Set) so concurrent calls sharing this same
                // cache key don't all independently hit the vendor's token endpoint on a cache
                // miss — see API_HARDENING_CHECKLIST.md Tier 2 (cache-stampede protection).
                var token = await tokenCache.GetOrCreateAsync(cacheKey, async exchangeCt =>
                {
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
                        var http = httpClientFactory.CreateClient("FlowEngine");
                        using var tokenResponse = await http.SendAsync(tokenRequest, exchangeCt);
                        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(exchangeCt);
                        var tokenEl   = JsonDocument.Parse(tokenBody).RootElement;
                        if (tokenEl.TryGetProperty(tokenField, out var tokenProp))
                        {
                            var tok = tokenProp.GetString() ?? tokenProp.ToString();
                            if (!string.IsNullOrEmpty(tok))
                                return (tok, OAuth2CacheKey.ComputeTtl(tokenEl, expiresInField));
                        }
                    }
                    catch { /* token exchange failed — request proceeds without auth */ }
                    return null;
                }, ct);

                if (token is not null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;
            }
            // hmac: requires request signing — not applied, matching ApiEndpointTestHelper.
        }
    }

    private static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";
}
