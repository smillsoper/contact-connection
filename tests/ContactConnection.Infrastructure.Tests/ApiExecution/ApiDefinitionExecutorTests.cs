using System.Net;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.ApiExecution;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.ApiExecution;

/// <summary>
/// Covers ApiDefinitionExecutor's own responsibilities — request building (method/query-param
/// merge/body/content-type), all four auth dispatch branches (api_key/bearer/basic/oauth2,
/// including the oauth2 cache hit vs. token-exchange-then-cache paths), and timeout/error
/// normalization. IVendorResilienceExecutor is mocked here since its own retry/circuit-breaker
/// behavior is already covered by VendorResilienceExecutorTests — this class isolates
/// ApiDefinitionExecutor's logic from it.
/// </summary>
public class ApiDefinitionExecutorTests
{
    private static ApiDefinitionExecutionRequest NewRequest(
        string method = "GET",
        string url = "https://vendor.example.com/op",
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? queryParams = null,
        string? body = null,
        string authConfigJson = "{\"type\":\"none\"}",
        int timeoutSeconds = 30,
        Func<string, CancellationToken, Task<string?>>? getCredential = null) =>
        new(method, url, headers ?? [], queryParams ?? [], body, authConfigJson, timeoutSeconds,
            getCredential ?? ((_, _) => Task.FromResult<string?>(null)));

    private static Mock<IVendorResilienceExecutor> MockResilience(
        HttpResponseMessage response, Action<HttpRequestMessage>? captureRequest = null)
    {
        var mock = new Mock<IVendorResilienceExecutor>();
        mock.Setup(r => r.SendAsync(
                It.IsAny<Guid>(), It.IsAny<HttpRequestMessage>(), It.IsAny<bool>(), It.IsAny<HttpClient>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, HttpRequestMessage, bool, HttpClient, CancellationToken>((_, req, _, _, _) =>
            {
                captureRequest?.Invoke(req);
                return Task.FromResult(response);
            });
        return mock;
    }

    private static ApiDefinitionExecutor CreateExecutor(
        Mock<IVendorResilienceExecutor> resilience,
        Mock<IOAuth2TokenCache>? tokenCache = null,
        HttpMessageHandler? tokenEndpointHandler = null)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("FlowEngine"))
            .Returns(() => new HttpClient(tokenEndpointHandler ?? new UnusedHandler()));

        return new ApiDefinitionExecutor(
            factoryMock.Object,
            (tokenCache ?? new Mock<IOAuth2TokenCache>()).Object,
            resilience.Object);
    }

    // ── Request building ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsBodyAndStatus()
    {
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("hello") });
        var executor = CreateExecutor(resilience);

        var result = await executor.ExecuteAsync(NewRequest());

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("hello", result.ResponseBody);
        Assert.False(result.TimedOut);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ErrorStatusCode_SuccessFalse_NoException()
    {
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("nope") });
        var executor = CreateExecutor(resilience);

        var result = await executor.ExecuteAsync(NewRequest());

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_MergesQueryParams_IntoRequestUrl()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(
            url: "https://vendor.example.com/op?existing=1",
            queryParams: new Dictionary<string, string> { ["foo"] = "bar" }));

        Assert.NotNull(captured);
        Assert.Contains("existing=1", captured!.RequestUri!.Query);
        Assert.Contains("foo=bar", captured.RequestUri!.Query);
    }

    [Fact]
    public async Task ExecuteAsync_SetsRequestBody_WithGivenContentType()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(
            method: "POST",
            headers: new Dictionary<string, string> { ["Content-Type"] = "application/xml" },
            body: "<a/>"));

        Assert.NotNull(captured?.Content);
        Assert.Equal("application/xml", captured!.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("<a/>", await captured.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownHttpMethod_DefaultsToGet()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(method: "TRACE"));

        Assert.Equal(HttpMethod.Get, captured!.Method);
    }

    // ── Auth dispatch ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ApiKeyAuth_HeaderPlacement_AddsHeader()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"api_key\",\"credentialKey\":\"k\",\"placement\":\"header\",\"headerName\":\"X-Api-Key\"}",
            getCredential: (_, _) => Task.FromResult<string?>("secret123")));

        Assert.Equal("secret123", captured!.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task ApiKeyAuth_QueryPlacement_AddsToQueryString()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"api_key\",\"credentialKey\":\"k\",\"addTo\":\"query\",\"paramName\":\"apikey\"}",
            getCredential: (_, _) => Task.FromResult<string?>("secret123")));

        Assert.Contains("apikey=secret123", captured!.RequestUri!.Query);
    }

    [Fact]
    public async Task ApiKeyAuth_CredentialMissing_SendsRequestWithoutIt()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        var result = await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"api_key\",\"credentialKey\":\"missing\",\"placement\":\"header\",\"headerName\":\"X-Api-Key\"}",
            getCredential: (_, _) => Task.FromResult<string?>(null)));

        Assert.True(result.Success); // a missing credential doesn't fail the call — it just proceeds unauthenticated
        Assert.False(captured!.Headers.Contains("X-Api-Key"));
    }

    [Fact]
    public async Task BearerAuth_SetsAuthorizationHeader()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"bearer\",\"tokenKey\":\"k\"}",
            getCredential: (_, _) => Task.FromResult<string?>("tok-abc")));

        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("tok-abc", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task BasicAuth_SetsBase64EncodedAuthorizationHeader()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"basic\",\"usernameKey\":\"u\",\"passwordKey\":\"p\"}",
            getCredential: (key, _) => Task.FromResult<string?>(key == "u" ? "alice" : "hunter2")));

        Assert.Equal("Basic", captured!.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:hunter2")), captured.Headers.Authorization.Parameter);
    }

    // ── oauth2 ────────────────────────────────────────────────────────────────
    // IOAuth2TokenCache itself is mocked here (GetOrCreateAsync) — its actual caching/
    // distributed-lock/stampede-protection mechanics are covered separately by
    // RedisOAuth2TokenCacheTests against a real Redis. These tests only verify
    // ApiDefinitionExecutor calls GetOrCreateAsync (not a bare Get-then-Set) and correctly
    // applies whatever token it returns.

    /// <summary>Simulates a cache hit: GetOrCreateAsync returns the given token without ever
    /// invoking the exchange delegate.</summary>
    private static void SetupCacheHit(Mock<IOAuth2TokenCache> tokenCache, string cachedToken) =>
        tokenCache.Setup(c => c.GetOrCreateAsync(
                It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedToken);

    /// <summary>Simulates a cache miss: GetOrCreateAsync actually invokes the caller's exchange
    /// delegate and returns whatever it produces (null included) — same contract
    /// RedisOAuth2TokenCache.GetOrCreateAsync has on a miss with no lock contention.</summary>
    private static void SetupCacheMissInvokesExchange(Mock<IOAuth2TokenCache> tokenCache) =>
        tokenCache.Setup(c => c.GetOrCreateAsync(
                It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>>, CancellationToken>(async (_, exchange, ct) =>
                (await exchange(ct))?.Token);

    [Fact]
    public async Task Oauth2Auth_CachedToken_UsesCache_NeverCallsTokenEndpoint()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var tokenCache = new Mock<IOAuth2TokenCache>();
        SetupCacheHit(tokenCache, "cached-token");
        var tokenHandler = new CountingHandler(_ => throw new InvalidOperationException("token endpoint should not be called on a cache hit"));
        var executor = CreateExecutor(resilience, tokenCache, tokenHandler);

        await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"oauth2\",\"tokenUrl\":\"https://vendor.example.com/token\",\"clientIdKey\":\"id\",\"clientSecretKey\":\"secret\"}",
            getCredential: (key, _) => Task.FromResult<string?>(key == "id" ? "client-id" : "client-secret")));

        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("cached-token", captured.Headers.Authorization.Parameter);
        Assert.Equal(0, tokenHandler.CallCount);
    }

    [Fact]
    public async Task Oauth2Auth_NoCachedToken_ExchangesToken_SetsAuthHeader_ViaGetOrCreateAsync()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var tokenCache = new Mock<IOAuth2TokenCache>();
        SetupCacheMissInvokesExchange(tokenCache);
        var tokenHandler = new CountingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"fresh-token\",\"expires_in\":3600}", Encoding.UTF8, "application/json"),
            });
        var executor = CreateExecutor(resilience, tokenCache, tokenHandler);

        await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"oauth2\",\"tokenUrl\":\"https://vendor.example.com/token\",\"credentialPlacement\":\"header\",\"clientIdKey\":\"id\",\"clientSecretKey\":\"secret\"}",
            getCredential: (key, _) => Task.FromResult<string?>(key == "id" ? "client-id" : "client-secret")));

        Assert.Equal(1, tokenHandler.CallCount);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("fresh-token", captured.Headers.Authorization.Parameter);
        tokenCache.Verify(c => c.GetOrCreateAsync(
            It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Oauth2Auth_TokenExchangeFails_RequestProceedsWithoutAuth_NotThrown()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var tokenCache = new Mock<IOAuth2TokenCache>();
        SetupCacheMissInvokesExchange(tokenCache);
        var tokenHandler = new CountingHandler(_ => throw new HttpRequestException("token endpoint unreachable"));
        var executor = CreateExecutor(resilience, tokenCache, tokenHandler);

        var result = await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"oauth2\",\"tokenUrl\":\"https://vendor.example.com/token\",\"clientIdKey\":\"id\",\"clientSecretKey\":\"secret\"}",
            getCredential: (key, _) => Task.FromResult<string?>(key == "id" ? "client-id" : "client-secret")));

        Assert.True(result.Success); // the vendor call itself still goes through, just unauthenticated
        Assert.Null(captured!.Headers.Authorization);
    }

    // ── Timeout / unexpected errors ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_TimesOut_ReturnsTimedOutResult_NotThrown()
    {
        var resilience = new Mock<IVendorResilienceExecutor>();
        resilience.Setup(r => r.SendAsync(
                It.IsAny<Guid>(), It.IsAny<HttpRequestMessage>(), It.IsAny<bool>(), It.IsAny<HttpClient>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, HttpRequestMessage, bool, HttpClient, CancellationToken>(async (_, _, _, _, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct); // never completes on its own — only the executor's internal timeout ends this
                return null!;
            });
        var executor = CreateExecutor(resilience);

        var result = await executor.ExecuteAsync(NewRequest(timeoutSeconds: 1));

        Assert.False(result.Success);
        Assert.True(result.TimedOut);
        Assert.Contains("timed out", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedException_ReturnsErrorResult_NotThrown()
    {
        var resilience = new Mock<IVendorResilienceExecutor>();
        resilience.Setup(r => r.SendAsync(
                It.IsAny<Guid>(), It.IsAny<HttpRequestMessage>(), It.IsAny<bool>(), It.IsAny<HttpClient>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vendor exploded"));
        var executor = CreateExecutor(resilience);

        var result = await executor.ExecuteAsync(NewRequest());

        Assert.False(result.Success);
        Assert.False(result.TimedOut);
        Assert.Equal("vendor exploded", result.Error);
    }

    /// <summary>Handler that always throws — used where the test asserts the token endpoint must
    /// never be invoked at all.</summary>
    private class UnusedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This handler should never be invoked in this test.");
    }

    private class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            await Task.Yield();
            return responder(request);
        }
    }
}
