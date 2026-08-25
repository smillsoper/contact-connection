using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.ApiExecution;
using ContactConnection.Infrastructure.Common;

namespace ContactConnection.Api.Endpoints;

public record RunEndpointTestRequest(
    string Path,
    string? HttpMethod,
    string? QueryParams,
    string? Headers,
    string? RequestBodyTemplate,
    string Namespace,
    Dictionary<string, string> TestData,
    /// <summary>Dot-separated response field paths to redact from the returned Body (e.g.
    /// ["ssn","customer.dob"]) — mirrors the saved endpoint's SensitiveResponseFields, but passed
    /// directly here rather than looked up server-side, since a Test click may be testing an
    /// in-progress, not-yet-saved endpoint form. Null/empty = no masking. See
    /// API_HARDENING_CHECKLIST.md Tier 3.</summary>
    List<string>? SensitiveResponseFields = null);

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
    /// tokenCache, resilience, and rateLimiter are deliberately optional and default to null/
    /// false: the admin/portal "Test" button call sites omit them so a test click always reflects
    /// live config and a live attempt — never a cached oauth2 token, never blocked by a tripped
    /// circuit breaker (an admin actively testing wants to know if a vendor has recovered, not be
    /// told "can't tell, circuit's open"), and never throttled by traffic the flow engine
    /// generated (a manual test click shouldn't fail because a live call a moment ago used up the
    /// definition's budget). FlowSessionsEndpoints' live address validation/ZIP/autocomplete
    /// resolution passes all three, since that traffic runs on every call, not once per manual
    /// click.</summary>
    public static async Task<RunEndpointTestResponse> RunTestAsync(
        string baseUrl,
        string authConfigJson,
        RunEndpointTestRequest req,
        Func<string, CancellationToken, Task<string?>> getCredential,
        IHttpClientFactory httpFactory,
        CancellationToken ct,
        IOAuth2TokenCache? tokenCache = null,
        IVendorResilienceExecutor? resilience = null,
        Guid definitionId = default,
        bool allowRetryOnAmbiguousFailure = false,
        IOutboundRateLimiter? rateLimiter = null,
        int? rateLimitPerMinute = null,
        IMtlsHttpClientProvider? mtlsProvider = null)
    {
        try
        {
            if (rateLimiter is not null)
            {
                var decision = await rateLimiter.TryAcquireAsync(definitionId, rateLimitPerMinute, ct);
                if (!decision.Allowed)
                    throw new RateLimitExceededException(definitionId, rateLimitPerMinute ?? 0, decision.RetryAfterSeconds);
            }

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

            string? bodyContentType = null;
            if (!string.IsNullOrEmpty(req.Headers))
            {
                try
                {
                    var hdrDict = JsonSerializer.Deserialize<Dictionary<string, string>>(req.Headers);
                    if (hdrDict != null)
                        foreach (var (k, v) in hdrDict)
                        {
                            var resolved = SubstituteVars(v, ns, data);
                            // Content-Type is a content header, not a request header —
                            // HttpRequestHeaders silently rejects it (TryAddWithoutValidation
                            // returns false, nothing is stored), so it has to be captured here and
                            // applied to the StringContent below instead of being read back off
                            // request.Headers, which would never find it.
                            if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase))
                            {
                                bodyContentType = resolved;
                                continue;
                            }
                            request.Headers.TryAddWithoutValidation(k, resolved);
                        }
                }
                catch { }
            }

            // Resolved before ApplyAuth (not after, as request.Content would be) so the hmac case
            // can sign it — either directly (no payloadTemplate configured) or as the fallback
            // behind a resolved payloadTemplate, computed the same way just below.
            var resolvedBody = !string.IsNullOrEmpty(req.RequestBodyTemplate)
                ? SubstituteVars(req.RequestBodyTemplate, ns, data)
                : null;
            var hmacPayload = ResolveHmacPayload(authConfigJson, ns, data);

            await ApplyAuth(request, uriBuilder, authConfigJson, getCredential, httpFactory, ct, tokenCache, hmacPayload ?? resolvedBody);
            request.RequestUri = uriBuilder.Uri;

            if (resolvedBody is not null)
                request.Content = new StringContent(resolvedBody, Encoding.UTF8, bodyContentType ?? "application/json");

            // mTLS identity is a transport-level property (the TLS handshake itself), not
            // something ApplyAuth can attach to `request` the way every other auth type's
            // headers/query params are — so the client itself has to be selected based on the
            // auth config, not the shared "FlowEngine" client used otherwise.
            var http = await ResolveHttpClientAsync(authConfigJson, getCredential, httpFactory, mtlsProvider, definitionId, ct);

            HttpResponseMessage response;
            if (resilience is not null)
            {
                // GET/HEAD/PUT/DELETE are always safe to retry on an ambiguous failure by HTTP
                // semantics; for POST/PATCH, only the endpoint's own IsRetrySafe opt-in
                // (allowRetryOnAmbiguousFailure) allows it.
                var alwaysRetrySafeMethod = method == HttpMethod.Get || method == HttpMethod.Head
                    || method == HttpMethod.Put || method == HttpMethod.Delete;
                response = await resilience.SendAsync(
                    definitionId, request, alwaysRetrySafeMethod || allowRetryOnAmbiguousFailure, http, ct);
            }
            else
            {
                response = await http.SendAsync(request, ct);
            }
            using var _ = response;
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            // Masked before the response is ever handed back to the browser — the caller passes
            // whatever's currently in the endpoint form, saved or not (see the field's own doc
            // comment on RunEndpointTestRequest).
            if (req.SensitiveResponseFields is { Count: > 0 } sensitiveFields)
                responseBody = ResponseFieldMasker.MaskJson(responseBody, sensitiveFields);

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
        CancellationToken ct,
        IMtlsHttpClientProvider? mtlsProvider = null)
    {
        // No tokenCache passed — the "Test" button always runs a live, uncached exchange.
        // mtlsProvider IS passed (unlike tokenCache/resilience/rateLimiter, which are
        // deliberately withheld from a manual test click) — without the right client
        // certificate, an mtls-configured vendor rejects the TLS handshake outright, so a Test
        // click means nothing for that auth type unless the cert is actually applied.
        var result = await RunTestAsync(baseUrl, authConfigJson, req, getCredential, httpFactory, ct, mtlsProvider: mtlsProvider);
        return Results.Ok(result);
    }

    /// <summary>Extracts the hmac auth type's optional payloadTemplate and resolves it through
    /// the same SubstituteVars/ns/data mechanism as the request body/headers/query params — so
    /// the signed string can pull in fields the vendor requires even when they aren't part of the
    /// outgoing body. Returns null when the auth type isn't "hmac" or no template is configured,
    /// meaning the caller falls back to signing the actual request body.</summary>
    private static string? ResolveHmacPayload(string authConfigJson, string ns, Dictionary<string, string> data)
    {
        try
        {
            var root = JsonDocument.Parse(authConfigJson).RootElement;
            if (!root.TryGetProperty("type", out var tp) || tp.GetString() != "hmac") return null;
            var template = root.TryGetProperty("payloadTemplate", out var pt) ? pt.GetString() : null;
            return string.IsNullOrEmpty(template) ? null : SubstituteVars(template, ns, data);
        }
        catch { return null; }
    }

    private static async Task ApplyAuth(
        HttpRequestMessage request,
        UriBuilder uriBuilder,
        string authConfigJson,
        Func<string, CancellationToken, Task<string?>> getCredential,
        IHttpClientFactory httpFactory,
        CancellationToken ct,
        IOAuth2TokenCache? tokenCache = null,
        string? signaturePayload = null)
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

                Task<(string Token, TimeSpan Ttl)?> Exchange(CancellationToken exchangeCt) => ExchangeTokenAsync(
                    tokenUrl, method, placement, clientId, clientSecret, scopes, bodyTemplate, clientIdKey, clientSecretKey,
                    contentType, tokenField, expiresInField, httpFactory, exchangeCt);

                string? token;
                if (tokenCache is not null)
                {
                    // GetOrCreateAsync (not a bare Get-then-Set) so concurrent calls sharing this
                    // same cache key don't all independently hit the vendor's token endpoint on a
                    // cache miss — see API_HARDENING_CHECKLIST.md Tier 2 (cache-stampede
                    // protection). Only reachable from FlowSessionsEndpoints' live resolution,
                    // which passes a tokenCache; the admin/portal "Test" button never does (see
                    // this method's doc comment), so a manual test click is never rate-limited by
                    // waiting on someone else's in-flight exchange.
                    var cacheKey = OAuth2CacheKey.Build(tokenUrl, method, placement, clientId, clientSecret, scopes, tokenField);
                    token = await tokenCache.GetOrCreateAsync(cacheKey, Exchange, ct);
                }
                else
                {
                    var result = await Exchange(ct);
                    token = result?.Token;
                }

                if (token is not null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;
            }
            case "hmac":
            {
                var algorithm = Str(root, "algorithm") is { Length: > 0 } alg ? alg : "SHA256";
                var headerName = Str(root, "headerName") is { Length: > 0 } hn ? hn : "X-Signature";
                var includeTimestamp = root.TryGetProperty("includeTimestamp", out var itsEl)
                    && itsEl.ValueKind == JsonValueKind.True;

                var secret = await getCredential(Str(root, "secretKey"), ct);
                if (secret is null) break;

                var signature = HmacSigner.ComputeSignatureHeaderValue(algorithm, secret, signaturePayload, includeTimestamp);
                request.Headers.TryAddWithoutValidation(headerName, signature);
                break;
            }
            case "aws_sigv4":
            {
                var accessKeyId = await getCredential(Str(root, "accessKeyIdKey"), ct);
                var secretAccessKey = await getCredential(Str(root, "secretAccessKeyKey"), ct);
                if (accessKeyId is null || secretAccessKey is null) break;

                var sessionTokenKey = Str(root, "sessionTokenKey");
                var sessionToken = sessionTokenKey.Length > 0 ? await getCredential(sessionTokenKey, ct) : null;
                var region = Str(root, "region");
                var service = Str(root, "service");

                var host = uriBuilder.Uri.IsDefaultPort ? uriBuilder.Host : $"{uriBuilder.Host}:{uriBuilder.Port}";
                var sig = AwsSigV4Signer.Sign(
                    request.Method.Method, uriBuilder.Path, uriBuilder.Query, host, signaturePayload,
                    accessKeyId, secretAccessKey, sessionToken, region, service, DateTimeOffset.UtcNow);

                request.Headers.TryAddWithoutValidation("X-Amz-Date", sig.AmzDate);
                if (sig.SecurityToken is not null)
                    request.Headers.TryAddWithoutValidation("X-Amz-Security-Token", sig.SecurityToken);
                request.Headers.TryAddWithoutValidation("Authorization", sig.AuthorizationHeader);
                break;
            }
            // "mtls" intentionally has no case here — the client certificate is applied to the
            // HttpClient itself (see ResolveHttpClientAsync), not to this HttpRequestMessage.
            // There are no headers/query params for this auth type to add.
        }
    }

    /// <summary>Selects the HttpClient to send with: the shared "FlowEngine" client for every
    /// auth type except "mtls", which needs its own client carrying the configured client
    /// certificate. Falls back to the shared client if mtlsProvider wasn't supplied, or if the
    /// cert credential is missing or unusable — same "proceed unauthenticated" precedent every
    /// other auth type follows when its credential can't be resolved.</summary>
    private static async Task<HttpClient> ResolveHttpClientAsync(
        string authConfigJson,
        Func<string, CancellationToken, Task<string?>> getCredential,
        IHttpClientFactory httpFactory,
        IMtlsHttpClientProvider? mtlsProvider,
        Guid definitionId,
        CancellationToken ct)
    {
        if (mtlsProvider is null) return httpFactory.CreateClient("FlowEngine");

        JsonElement root;
        try { root = JsonDocument.Parse(authConfigJson).RootElement; }
        catch { return httpFactory.CreateClient("FlowEngine"); }

        if (!root.TryGetProperty("type", out var tp) || tp.GetString() != "mtls")
            return httpFactory.CreateClient("FlowEngine");

        var certKey = Str(root, "certKey");
        var certBase64 = await getCredential(certKey, ct);
        if (certBase64 is null) return httpFactory.CreateClient("FlowEngine");

        byte[] certBytes;
        try { certBytes = Convert.FromBase64String(certBase64); }
        catch (FormatException) { return httpFactory.CreateClient("FlowEngine"); }

        var certPasswordKey = Str(root, "certPasswordKey");
        var certPassword = certPasswordKey.Length > 0 ? await getCredential(certPasswordKey, ct) : null;

        return mtlsProvider.GetClient(definitionId, certBytes, certPassword)
            ?? httpFactory.CreateClient("FlowEngine");
    }

    /// <summary>Builds and sends the client_credentials token request, and parses the configured
    /// tokenField/expiresInField out of the response. Returns null on any failure (malformed
    /// response, missing field, network error) — the caller proceeds unauthenticated rather than
    /// failing the whole test, matching this method's pre-existing behavior.</summary>
    private static async Task<(string Token, TimeSpan Ttl)?> ExchangeTokenAsync(
        string tokenUrl, string method, string placement, string clientId, string clientSecret, string scopes,
        string bodyTemplate, string clientIdKey, string clientSecretKey, string contentType,
        string tokenField, string expiresInField, IHttpClientFactory httpFactory, CancellationToken ct)
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
            var http = httpFactory.CreateClient("FlowEngine");
            using var tokenResponse = await http.SendAsync(tokenRequest, ct);
            var tokenBody = await tokenResponse.Content.ReadAsStringAsync(ct);
            var tokenEl   = JsonDocument.Parse(tokenBody).RootElement;
            if (tokenEl.TryGetProperty(tokenField, out var tokenProp))
            {
                var token = tokenProp.GetString() ?? tokenProp.ToString();
                if (!string.IsNullOrEmpty(token))
                    return (token, OAuth2CacheKey.ComputeTtl(tokenEl, expiresInField));
            }
        }
        catch { /* token exchange failed — request proceeds without auth */ }
        return null;
    }

    private static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";
}
