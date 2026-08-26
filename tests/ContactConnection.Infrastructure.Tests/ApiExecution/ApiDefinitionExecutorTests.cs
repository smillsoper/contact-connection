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

    private static Mock<IOutboundRateLimiter> MockRateLimiter(bool allow = true)
    {
        var mock = new Mock<IOutboundRateLimiter>();
        mock.Setup(r => r.TryAcquireAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allow ? RateLimitDecision.Allow : new RateLimitDecision(false, 5));
        return mock;
    }

    private static ApiDefinitionExecutor CreateExecutor(
        Mock<IVendorResilienceExecutor> resilience,
        Mock<IOAuth2TokenCache>? tokenCache = null,
        HttpMessageHandler? tokenEndpointHandler = null,
        Mock<IOutboundRateLimiter>? rateLimiter = null,
        Mock<IMtlsHttpClientProvider>? mtlsProvider = null)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("FlowEngine"))
            .Returns(() => new HttpClient(tokenEndpointHandler ?? new UnusedHandler()));

        return new ApiDefinitionExecutor(
            factoryMock.Object,
            (tokenCache ?? new Mock<IOAuth2TokenCache>()).Object,
            resilience.Object,
            (rateLimiter ?? MockRateLimiter()).Object,
            (mtlsProvider ?? new Mock<IMtlsHttpClientProvider>()).Object);
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

    // ── hmac ──────────────────────────────────────────────────────────────────
    // HmacSigner's own signing math (algorithms, timestamp format) is covered separately by
    // HmacSignerTests — these only verify ApiDefinitionExecutor picks the right payload to sign
    // and applies it to the right header.

    [Fact]
    public async Task HmacAuth_NoHmacPayload_SignsRequestBody()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(
            body: "the-request-body",
            authConfigJson: "{\"type\":\"hmac\",\"algorithm\":\"SHA256\",\"secretKey\":\"k\",\"headerName\":\"X-Sig\",\"includeTimestamp\":false}",
            getCredential: (_, _) => Task.FromResult<string?>("sekret")));

        var expected = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "the-request-body", includeTimestamp: false);
        Assert.Equal(expected, captured!.Headers.GetValues("X-Sig").Single());
    }

    [Fact]
    public async Task HmacAuth_HmacPayloadGiven_SignsThatInsteadOfBody()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        var request = NewRequest(
            body: "the-request-body",
            authConfigJson: "{\"type\":\"hmac\",\"algorithm\":\"SHA256\",\"secretKey\":\"k\",\"headerName\":\"X-Sig\",\"includeTimestamp\":false}",
            getCredential: (_, _) => Task.FromResult<string?>("sekret")) with { HmacPayload = "a-different-payload" };

        await executor.ExecuteAsync(request);

        var expected = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "a-different-payload", includeTimestamp: false);
        Assert.Equal(expected, captured!.Headers.GetValues("X-Sig").Single());
    }

    [Fact]
    public async Task HmacAuth_CredentialMissing_SendsRequestWithoutSignatureHeader()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        var result = await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"hmac\",\"secretKey\":\"missing\",\"headerName\":\"X-Sig\"}",
            getCredential: (_, _) => Task.FromResult<string?>(null)));

        Assert.True(result.Success);
        Assert.False(captured!.Headers.Contains("X-Sig"));
    }

    [Fact]
    public async Task HmacAuth_DefaultsAlgorithmAndHeaderName_WhenNotConfigured()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(
            body: "b",
            authConfigJson: "{\"type\":\"hmac\",\"secretKey\":\"k\"}", // no algorithm/headerName configured
            getCredential: (_, _) => Task.FromResult<string?>("sekret")));

        var expected = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "b", includeTimestamp: false);
        Assert.Equal(expected, captured!.Headers.GetValues("X-Signature").Single()); // "X-Signature" is the documented default
    }

    // ── aws_sigv4 ─────────────────────────────────────────────────────────────
    // AwsSigV4Signer's own signing math is covered separately by AwsSigV4SignerTests (against
    // the official AWS test vectors) — these only verify ApiDefinitionExecutor resolves the
    // right credentials and applies the resulting headers.

    [Fact]
    public async Task AwsSigV4Auth_AppliesAuthorizationAndDateHeaders_MatchingIndependentComputation()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(
            url: "https://vendor.example.com/orders",
            authConfigJson: "{\"type\":\"aws_sigv4\",\"accessKeyIdKey\":\"akid\",\"secretAccessKeyKey\":\"secret\",\"region\":\"us-east-1\",\"service\":\"execute-api\"}",
            getCredential: (key, _) => Task.FromResult<string?>(key == "akid" ? "AKIDEXAMPLE" : "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY")));

        Assert.True(captured!.Headers.Contains("X-Amz-Date"));
        Assert.False(captured.Headers.Contains("X-Amz-Security-Token"));
        var authHeader = captured.Headers.GetValues("Authorization").Single();
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/", authHeader);
        Assert.Contains("us-east-1/execute-api/aws4_request", authHeader);
        Assert.Contains("SignedHeaders=host;x-amz-date,", authHeader);
    }

    [Fact]
    public async Task AwsSigV4Auth_CredentialMissing_SendsRequestWithoutAuthHeader()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        var result = await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"aws_sigv4\",\"accessKeyIdKey\":\"missing\",\"secretAccessKeyKey\":\"missing\",\"region\":\"us-east-1\",\"service\":\"execute-api\"}",
            getCredential: (_, _) => Task.FromResult<string?>(null)));

        Assert.True(result.Success);
        Assert.False(captured!.Headers.Contains("Authorization"));
        Assert.False(captured.Headers.Contains("X-Amz-Date"));
    }

    [Fact]
    public async Task AwsSigV4Auth_SessionTokenConfigured_AddsSecurityTokenHeader_AndIsSignedHeader()
    {
        HttpRequestMessage? captured = null;
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }, r => captured = r);
        var executor = CreateExecutor(resilience);

        await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"aws_sigv4\",\"accessKeyIdKey\":\"akid\",\"secretAccessKeyKey\":\"secret\",\"sessionTokenKey\":\"token\",\"region\":\"us-east-1\",\"service\":\"execute-api\"}",
            getCredential: (key, _) => Task.FromResult<string?>(key switch
            {
                "akid" => "AKIDEXAMPLE",
                "secret" => "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY",
                "token" => "a-session-token",
                _ => null,
            })));

        Assert.Equal("a-session-token", captured!.Headers.GetValues("X-Amz-Security-Token").Single());
        Assert.Contains("SignedHeaders=host;x-amz-date;x-amz-security-token,", captured.Headers.GetValues("Authorization").Single());
    }

    // ── mtls ──────────────────────────────────────────────────────────────────
    // The certificate itself is applied to the HttpClient (via IMtlsHttpClientProvider), not to
    // the HttpRequestMessage — so these verify the right client reaches the resilience executor,
    // not any header.

    [Fact]
    public async Task MtlsAuth_CertResolves_UsesTheProvidedClient_NotTheSharedOne()
    {
        // resilience.SendAsync is fully mocked below (never actually calls through to the client's
        // real handler), so the client just needs to be a distinguishable instance to verify against.
        var mtlsClient = new HttpClient(new UnusedHandler());
        var mtlsProvider = new Mock<IMtlsHttpClientProvider>();
        mtlsProvider.Setup(p => p.GetClient(It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string?>()))
            .Returns(mtlsClient);

        var resilience = new Mock<IVendorResilienceExecutor>();
        resilience.Setup(r => r.SendAsync(
                It.IsAny<Guid>(), It.IsAny<HttpRequestMessage>(), It.IsAny<bool>(), It.IsAny<HttpClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });

        var executor = CreateExecutor(resilience, mtlsProvider: mtlsProvider);

        var certBase64 = Convert.ToBase64String("fake-pfx-bytes"u8.ToArray());
        await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"mtls\",\"certKey\":\"cert\"}",
            getCredential: (_, _) => Task.FromResult<string?>(certBase64)));

        resilience.Verify(r => r.SendAsync(
            It.IsAny<Guid>(), It.IsAny<HttpRequestMessage>(), It.IsAny<bool>(),
            It.Is<HttpClient>(c => ReferenceEquals(c, mtlsClient)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MtlsAuth_CredentialMissing_FallsBackToSharedClient_NeverCallsProvider()
    {
        var mtlsProvider = new Mock<IMtlsHttpClientProvider>();
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        var executor = CreateExecutor(resilience, mtlsProvider: mtlsProvider);

        var result = await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"mtls\",\"certKey\":\"missing\"}",
            getCredential: (_, _) => Task.FromResult<string?>(null)));

        Assert.True(result.Success);
        mtlsProvider.Verify(p => p.GetClient(It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task MtlsAuth_ProviderReturnsNull_FallsBackToSharedClient_RequestStillSucceeds()
    {
        var mtlsProvider = new Mock<IMtlsHttpClientProvider>();
        mtlsProvider.Setup(p => p.GetClient(It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string?>()))
            .Returns((HttpClient?)null); // bad password / corrupt PKCS#12
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
        var executor = CreateExecutor(resilience, mtlsProvider: mtlsProvider);

        var result = await executor.ExecuteAsync(NewRequest(
            authConfigJson: "{\"type\":\"mtls\",\"certKey\":\"cert\"}",
            getCredential: (_, _) => Task.FromResult<string?>(Convert.ToBase64String("fake"u8.ToArray()))));

        Assert.True(result.Success);
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

    // ── Rate limiting ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RateLimitDenied_ReturnsErrorResult_NeverCallsResilience()
    {
        var resilience = new Mock<IVendorResilienceExecutor>();
        var rateLimiter = MockRateLimiter(allow: false);
        var executor = CreateExecutor(resilience, rateLimiter: rateLimiter);

        var result = await executor.ExecuteAsync(NewRequest());

        Assert.False(result.Success);
        Assert.False(result.TimedOut);
        Assert.Contains("Rate limit exceeded", result.Error);
        resilience.Verify(r => r.SendAsync(
            It.IsAny<Guid>(), It.IsAny<HttpRequestMessage>(), It.IsAny<bool>(), It.IsAny<HttpClient>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_RateLimitAllowed_PassesDefinitionIdAndLimitToLimiter()
    {
        var definitionId = Guid.NewGuid();
        var resilience = MockResilience(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        var rateLimiter = MockRateLimiter();
        var executor = CreateExecutor(resilience, rateLimiter: rateLimiter);

        var request = NewRequest() with { DefinitionId = definitionId, RateLimitPerMinute = 42 };
        var result = await executor.ExecuteAsync(request);

        Assert.True(result.Success);
        rateLimiter.Verify(r => r.TryAcquireAsync(definitionId, 42, It.IsAny<CancellationToken>()), Times.Once);
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
