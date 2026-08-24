using System.Net;
using System.Text;
using ContactConnection.Api.Endpoints;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.ApiExecution;
using Moq;
using Xunit;

namespace ContactConnection.Api.Tests.Endpoints;

/// <summary>
/// Covers ApiEndpointTestHelper — the admin/portal "Test" button and FlowSessionsEndpoints' live
/// address validation/ZIP/autocomplete resolution both funnel through RunTestAsync. This is the
/// sibling of ApiDefinitionExecutor (ContactConnection.Infrastructure.Tests), explicitly ported
/// from it, so the two test suites intentionally mirror each other's shape — including the
/// regression test for the Content-Type bug found and fixed in both places this session. See
/// API_HARDENING_CHECKLIST.md Tier 2.
/// </summary>
public class ApiEndpointTestHelperTests
{
    private static RunEndpointTestRequest NewRequest(
        string path = "/op",
        string? httpMethod = null,
        string? queryParams = null,
        string? headers = null,
        string? requestBodyTemplate = null,
        string ns = "address",
        Dictionary<string, string>? testData = null) =>
        new(path, httpMethod, queryParams, headers, requestBodyTemplate, ns, testData ?? []);

    private static Func<string, CancellationToken, Task<string?>> NoCredential =>
        (_, _) => Task.FromResult<string?>(null);

    private static Mock<IHttpClientFactory> MockFactory(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("FlowEngine")).Returns(() => new HttpClient(handler));
        return factory;
    }

    // ── SubstituteVars ────────────────────────────────────────────────────────

    [Fact]
    public void SubstituteVars_ReplacesMatchingNamespaceTag()
    {
        var result = ApiEndpointTestHelper.SubstituteVars(
            "/addresses/{{address.zip}}", "address", new Dictionary<string, string> { ["zip"] = "62701" });
        Assert.Equal("/addresses/62701", result);
    }

    [Fact]
    public void SubstituteVars_UnmatchedField_ResolvesToEmptyString()
    {
        var result = ApiEndpointTestHelper.SubstituteVars(
            "line2={{address.address2}}", "address", new Dictionary<string, string>());
        Assert.Equal("line2=", result);
    }

    [Fact]
    public void SubstituteVars_DifferentNamespace_LeftUntouched()
    {
        var result = ApiEndpointTestHelper.SubstituteVars(
            "{{flow.other}}", "address", new Dictionary<string, string> { ["other"] = "should-not-match" });
        Assert.Equal("{{flow.other}}", result);
    }

    [Fact]
    public void SubstituteVars_NullTemplate_ReturnsEmptyString()
    {
        Assert.Equal("", ApiEndpointTestHelper.SubstituteVars(null, "address", []));
    }

    // ── Request building ──────────────────────────────────────────────────────

    [Fact]
    public async Task RunTestAsync_ResolvesPathTemplate_UsingNamespaceData()
    {
        string? capturedPath = null;
        var handler = new FuncHandler(req =>
        {
            capturedPath = req.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        });

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}",
            NewRequest(path: "/addresses/{{address.zip}}", testData: new() { ["zip"] = "62701" }),
            NoCredential, MockFactory(handler).Object, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("/addresses/62701", capturedPath);
    }

    [Fact]
    public async Task RunTestAsync_MergesQueryParams_HonoringSkipIfEmpty()
    {
        string? capturedQuery = null;
        var handler = new FuncHandler(req =>
        {
            capturedQuery = req.RequestUri!.Query;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        });

        var queryParamsJson = "{\"zip\":\"{{address.zip}}\",\"unit\":\"{{address.unit}}\",\"_skipIfEmpty\":[\"unit\"]}";

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}",
            NewRequest(queryParams: queryParamsJson, testData: new() { ["zip"] = "62701" }), // unit left blank
            NoCredential, MockFactory(handler).Object, CancellationToken.None);

        Assert.Contains("zip=62701", capturedQuery);
        Assert.DoesNotContain("unit=", capturedQuery); // _skipIfEmpty dropped the blank field entirely
    }

    [Fact]
    public async Task RunTestAsync_MalformedQueryParamsJson_SilentlyIgnored_RequestStillSucceeds()
    {
        var handler = new FuncHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }));

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}",
            NewRequest(queryParams: "{not valid json"),
            NoCredential, MockFactory(handler).Object, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task RunTestAsync_MalformedHeadersJson_SilentlyIgnored_RequestStillSucceeds()
    {
        var handler = new FuncHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }));

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}",
            NewRequest(headers: "{not valid json"),
            NoCredential, MockFactory(handler).Object, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task RunTestAsync_BodyContentType_AppliesConfiguredHeader_NotDefaultJson()
    {
        // Regression test for the bug found (and fixed, in both this file and ApiDefinitionExecutor)
        // this session: Content-Type set via the Headers config previously never took effect
        // because HttpRequestHeaders.TryAddWithoutValidation("Content-Type", ...) silently drops it.
        HttpRequestMessage? captured = null;
        var handler = new FuncHandler(req =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        });

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}",
            NewRequest(httpMethod: "POST", headers: "{\"Content-Type\":\"application/xml\"}", requestBodyTemplate: "<a/>"),
            NoCredential, MockFactory(handler).Object, CancellationToken.None);

        Assert.Equal("application/xml", captured!.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("<a/>", await captured.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RunTestAsync_NoBodyContentTypeConfigured_DefaultsToJson()
    {
        HttpRequestMessage? captured = null;
        var handler = new FuncHandler(req =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        });

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}",
            NewRequest(httpMethod: "POST", requestBodyTemplate: "{}"),
            NoCredential, MockFactory(handler).Object, CancellationToken.None);

        Assert.Equal("application/json", captured!.Content!.Headers.ContentType!.MediaType);
    }

    // ── Response handling ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunTestAsync_JsonResponseBody_IsPrettyPrinted()
    {
        var handler = new FuncHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"a\":1}") }));

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}", NewRequest(),
            NoCredential, MockFactory(handler).Object, CancellationToken.None);

        Assert.NotNull(result.Body);
        Assert.Contains('\n', result.Body); // WriteIndented adds line breaks a compact JSON string never has
        Assert.Contains("\"a\": 1", result.Body);
    }

    [Fact]
    public async Task RunTestAsync_NonJsonResponseBody_PassesThroughUnchanged()
    {
        var handler = new FuncHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("plain text, not json") }));

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}", NewRequest(),
            NoCredential, MockFactory(handler).Object, CancellationToken.None);

        Assert.Equal("plain text, not json", result.Body);
    }

    [Fact]
    public async Task RunTestAsync_ResolvedUrl_ReflectedInResult()
    {
        var handler = new FuncHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }));

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}", NewRequest(path: "/ping"),
            NoCredential, MockFactory(handler).Object, CancellationToken.None);

        // UriBuilder.ToString() always includes an explicit port, even the default for the scheme.
        Assert.Equal("https://vendor.example.com:443/ping", result.ResolvedUrl);
    }

    // ── Auth dispatch ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ApiKeyAuth_HeaderPlacement_AddsHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new FuncHandler(req => { captured = req; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }); });

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"api_key\",\"credentialKey\":\"k\",\"placement\":\"header\",\"headerName\":\"X-Api-Key\"}",
            NewRequest(), (_, _) => Task.FromResult<string?>("secret123"), MockFactory(handler).Object, CancellationToken.None);

        Assert.Equal("secret123", captured!.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task ApiKeyAuth_QueryPlacement_AddsToQueryString()
    {
        HttpRequestMessage? captured = null;
        var handler = new FuncHandler(req => { captured = req; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }); });

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"api_key\",\"credentialKey\":\"k\",\"addTo\":\"query\",\"paramName\":\"apikey\"}",
            NewRequest(), (_, _) => Task.FromResult<string?>("secret123"), MockFactory(handler).Object, CancellationToken.None);

        Assert.Contains("apikey=secret123", captured!.RequestUri!.Query);
    }

    [Fact]
    public async Task BearerAuth_SetsAuthorizationHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new FuncHandler(req => { captured = req; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }); });

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"bearer\",\"tokenKey\":\"k\"}",
            NewRequest(), (_, _) => Task.FromResult<string?>("tok-abc"), MockFactory(handler).Object, CancellationToken.None);

        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("tok-abc", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task BasicAuth_SetsBase64EncodedAuthorizationHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new FuncHandler(req => { captured = req; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }); });

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"basic\",\"usernameKey\":\"u\",\"passwordKey\":\"p\"}",
            NewRequest(), (key, _) => Task.FromResult<string?>(key == "u" ? "alice" : "hunter2"),
            MockFactory(handler).Object, CancellationToken.None);

        Assert.Equal("Basic", captured!.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:hunter2")), captured.Headers.Authorization.Parameter);
    }

    // ── hmac ──────────────────────────────────────────────────────────────────
    // HmacSigner's own signing math is covered by HmacSignerTests (Infrastructure.Tests). What's
    // unique to this layer is payloadTemplate resolution — it goes through SubstituteVars/ns/data
    // exactly like the request body/query params, which ApiDefinitionExecutor (the flow-engine
    // sibling, already resolved by the time it sees anything) never has to do itself.

    [Fact]
    public async Task HmacAuth_NoPayloadTemplate_SignsResolvedRequestBody()
    {
        HttpRequestMessage? captured = null;
        var handler = new FuncHandler(req => { captured = req; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }); });

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com",
            "{\"type\":\"hmac\",\"algorithm\":\"SHA256\",\"secretKey\":\"k\",\"headerName\":\"X-Sig\",\"includeTimestamp\":false}",
            NewRequest(requestBodyTemplate: "body-{{address.zip}}", testData: new() { ["zip"] = "62701" }),
            (_, _) => Task.FromResult<string?>("sekret"),
            MockFactory(handler).Object, CancellationToken.None);

        var expected = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "body-62701", includeTimestamp: false);
        Assert.Equal(expected, captured!.Headers.GetValues("X-Sig").Single());
    }

    [Fact]
    public async Task HmacAuth_PayloadTemplateConfigured_SignsResolvedTemplate_NotTheBody()
    {
        HttpRequestMessage? captured = null;
        var handler = new FuncHandler(req => { captured = req; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }); });

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com",
            "{\"type\":\"hmac\",\"algorithm\":\"SHA256\",\"secretKey\":\"k\",\"headerName\":\"X-Sig\"," +
            "\"includeTimestamp\":false,\"payloadTemplate\":\"{{address.orderId}}:{{address.total}}\"}",
            NewRequest(requestBodyTemplate: "unrelated-body", testData: new() { ["orderId"] = "555", ["total"] = "19.99" }),
            (_, _) => Task.FromResult<string?>("sekret"),
            MockFactory(handler).Object, CancellationToken.None);

        var expected = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "555:19.99", includeTimestamp: false);
        Assert.Equal(expected, captured!.Headers.GetValues("X-Sig").Single());
    }

    [Fact]
    public async Task HmacAuth_CredentialMissing_SendsRequestWithoutSignatureHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = new FuncHandler(req => { captured = req; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }); });

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"hmac\",\"secretKey\":\"missing\",\"headerName\":\"X-Sig\"}",
            NewRequest(), (_, _) => Task.FromResult<string?>(null),
            MockFactory(handler).Object, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(captured!.Headers.Contains("X-Sig"));
    }

    // ── oauth2 — the admin/portal "Test" button never passes a tokenCache (always live) ────────

    [Fact]
    public async Task Oauth2Auth_NoTokenCachePassed_AlwaysExchanges_EvenOnRepeatedCalls()
    {
        var exchangeCount = 0;
        var handler = new FuncHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/token")
            {
                exchangeCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"fresh\",\"expires_in\":3600}", Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        });
        var factory = MockFactory(handler).Object;
        var authConfig = "{\"type\":\"oauth2\",\"tokenUrl\":\"https://vendor.example.com/token\",\"clientIdKey\":\"id\",\"clientSecretKey\":\"secret\"}";
        var getCred = (string key, CancellationToken _) => Task.FromResult<string?>(key == "id" ? "client-id" : "client-secret");

        await ApiEndpointTestHelper.RunTestAsync("https://vendor.example.com", authConfig, NewRequest(), getCred, factory, CancellationToken.None);
        await ApiEndpointTestHelper.RunTestAsync("https://vendor.example.com", authConfig, NewRequest(), getCred, factory, CancellationToken.None);

        Assert.Equal(2, exchangeCount); // no tokenCache passed → every call is a fresh, live exchange
    }

    /// <summary>Simulates a cache hit: GetOrCreateAsync returns the given token without ever
    /// invoking the exchange delegate. IOAuth2TokenCache's actual caching/distributed-lock/
    /// stampede-protection mechanics are covered separately in
    /// ContactConnection.Infrastructure.Tests (RedisOAuth2TokenCacheTests) against a real Redis —
    /// these tests only verify RunTestAsync calls GetOrCreateAsync (not a bare Get-then-Set) and
    /// applies whatever token comes back.</summary>
    private static void SetupCacheHit(Mock<IOAuth2TokenCache> tokenCache, string cachedToken) =>
        tokenCache.Setup(c => c.GetOrCreateAsync(
                It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedToken);

    /// <summary>Simulates a cache miss: GetOrCreateAsync actually invokes the exchange delegate.</summary>
    private static void SetupCacheMissInvokesExchange(Mock<IOAuth2TokenCache> tokenCache) =>
        tokenCache.Setup(c => c.GetOrCreateAsync(
                It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>>, CancellationToken>(async (_, exchange, ct) =>
                (await exchange(ct))?.Token);

    [Fact]
    public async Task Oauth2Auth_WithTokenCache_CacheHit_SkipsExchangeEntirely()
    {
        var handler = new FuncHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/token")
                throw new InvalidOperationException("token endpoint should not be called on a cache hit");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        });
        var tokenCache = new Mock<IOAuth2TokenCache>();
        SetupCacheHit(tokenCache, "cached-token");

        HttpRequestMessage? captured = null;
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("FlowEngine")).Returns(() => new HttpClient(handler));

        // Capture the vendor request via a mocked resilience so we can assert the Authorization
        // header without also needing to distinguish it from the (never-made) token call in the
        // handler above.
        var resilience = new Mock<IVendorResilienceExecutor>();
        resilience.Setup(r => r.SendAsync(It.IsAny<Guid>(), It.IsAny<HttpRequestMessage>(), It.IsAny<bool>(), It.IsAny<HttpClient>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, HttpRequestMessage, bool, HttpClient, CancellationToken>((_, req, _, _, _) =>
            {
                captured = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
            });

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"oauth2\",\"tokenUrl\":\"https://vendor.example.com/token\",\"clientIdKey\":\"id\",\"clientSecretKey\":\"secret\"}",
            NewRequest(), (key, _) => Task.FromResult<string?>(key == "id" ? "client-id" : "client-secret"),
            factory.Object, CancellationToken.None, tokenCache.Object, resilience.Object);

        Assert.Equal("cached-token", captured!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Oauth2Auth_WithTokenCache_CacheMiss_ExchangesToken_AndCachesIt()
    {
        var handler = new FuncHandler(req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"fresh-token\",\"expires_in\":3600}", Encoding.UTF8, "application/json"),
        }));
        var tokenCache = new Mock<IOAuth2TokenCache>();
        SetupCacheMissInvokesExchange(tokenCache);

        HttpRequestMessage? captured = null;
        var resilience = new Mock<IVendorResilienceExecutor>();
        resilience.Setup(r => r.SendAsync(It.IsAny<Guid>(), It.IsAny<HttpRequestMessage>(), It.IsAny<bool>(), It.IsAny<HttpClient>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, HttpRequestMessage, bool, HttpClient, CancellationToken>((_, req, _, _, _) =>
            {
                captured = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
            });

        await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"oauth2\",\"tokenUrl\":\"https://vendor.example.com/token\",\"clientIdKey\":\"id\",\"clientSecretKey\":\"secret\"}",
            NewRequest(), (key, _) => Task.FromResult<string?>(key == "id" ? "client-id" : "client-secret"),
            MockFactory(handler).Object, CancellationToken.None, tokenCache.Object, resilience.Object);

        Assert.Equal("fresh-token", captured!.Headers.Authorization!.Parameter);
        tokenCache.Verify(c => c.GetOrCreateAsync(
            It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── resilience dispatch ───────────────────────────────────────────────────

    [Fact]
    public async Task NoResilience_SendsDirectlyViaHttpClient()
    {
        var handler = new FuncHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("direct") }));

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}", NewRequest(),
            NoCredential, MockFactory(handler).Object, CancellationToken.None); // resilience omitted

        Assert.Equal("direct", result.Body);
    }

    [Fact]
    public async Task WithResilience_DelegatesToResilienceExecutor_NotTheRawClient()
    {
        var handler = new FuncHandler(_ => throw new InvalidOperationException("the raw client must not be used for the main vendor call when resilience is supplied"));
        var resilience = new Mock<IVendorResilienceExecutor>();
        resilience.Setup(r => r.SendAsync(It.IsAny<Guid>(), It.IsAny<HttpRequestMessage>(), It.IsAny<bool>(), It.IsAny<HttpClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("via-resilience") });

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}", NewRequest(),
            NoCredential, MockFactory(handler).Object, CancellationToken.None, resilience: resilience.Object);

        Assert.Equal("via-resilience", result.Body);
    }

    // ── rate limiting ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NoRateLimiter_NeverConsulted_SendsDirectly()
    {
        var handler = new FuncHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("direct") }));

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}", NewRequest(),
            NoCredential, MockFactory(handler).Object, CancellationToken.None); // rateLimiter omitted

        Assert.Equal("direct", result.Body);
    }

    [Fact]
    public async Task RateLimiterDenies_ReturnsErrorResult_NeverSendsTheRequest()
    {
        var handler = new FuncHandler(_ => throw new InvalidOperationException("must never be called when the rate limiter denies"));
        var rateLimiter = new Mock<IOutboundRateLimiter>();
        rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitDecision(false, 7));

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}", NewRequest(),
            NoCredential, MockFactory(handler).Object, CancellationToken.None, rateLimiter: rateLimiter.Object);

        Assert.False(result.Success);
        Assert.Contains("Rate limit exceeded", result.Error);
    }

    [Fact]
    public async Task RateLimiterAllows_RequestProceedsNormally()
    {
        var handler = new FuncHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("allowed") }));
        var rateLimiter = new Mock<IOutboundRateLimiter>();
        rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateLimitDecision.Allow);

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}", NewRequest(),
            NoCredential, MockFactory(handler).Object, CancellationToken.None, rateLimiter: rateLimiter.Object);

        Assert.True(result.Success);
        Assert.Equal("allowed", result.Body);
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [Fact]
    public async Task UnderlyingSendThrows_ReturnsErrorResult_NotThrown()
    {
        var handler = new FuncHandler(_ => throw new HttpRequestException("vendor unreachable"));

        var result = await ApiEndpointTestHelper.RunTestAsync(
            "https://vendor.example.com", "{\"type\":\"none\"}", NewRequest(),
            NoCredential, MockFactory(handler).Object, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.StatusCode);
        Assert.Contains("vendor unreachable", result.Error);
    }

    private class FuncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return await responder(request);
        }
    }
}
