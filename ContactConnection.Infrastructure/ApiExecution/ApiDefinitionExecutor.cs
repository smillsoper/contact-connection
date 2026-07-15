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
public class ApiDefinitionExecutor(IHttpClientFactory httpClientFactory) : IApiDefinitionExecutor
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
            foreach (var (key, value) in request.Headers)
                httpRequest.Headers.TryAddWithoutValidation(key, value);

            await ApplyAuthAsync(httpRequest, uriBuilder, request.AuthConfigJson, request.GetCredential, linkedCts.Token);
            httpRequest.RequestUri = uriBuilder.Uri;

            if (request.Body is not null)
            {
                var contentType = httpRequest.Headers.TryGetValues("Content-Type", out var ctVals)
                    ? ctVals.FirstOrDefault() ?? "application/json"
                    : "application/json";
                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, contentType);
            }

            var http = httpClientFactory.CreateClient("FlowEngine");
            using var response = await http.SendAsync(httpRequest, linkedCts.Token);
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
                var scopes          = Str(root, "scopes");

                if (string.IsNullOrWhiteSpace(tokenUrl)) break;

                var clientId     = await getCredential(clientIdKey, ct);
                var clientSecret = await getCredential(clientSecretKey, ct);
                if (clientId is null || clientSecret is null) break;

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
                    using var tokenResponse = await http.SendAsync(tokenRequest, ct);
                    var tokenBody = await tokenResponse.Content.ReadAsStringAsync(ct);
                    var tokenEl   = JsonDocument.Parse(tokenBody).RootElement;
                    if (tokenEl.TryGetProperty(tokenField, out var tokenProp))
                    {
                        var token = tokenProp.GetString() ?? tokenProp.ToString();
                        if (!string.IsNullOrEmpty(token))
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                }
                catch { /* token exchange failed — request proceeds without auth */ }
                break;
            }
            // hmac: requires request signing — not applied, matching ApiEndpointTestHelper.
        }
    }

    private static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";
}
