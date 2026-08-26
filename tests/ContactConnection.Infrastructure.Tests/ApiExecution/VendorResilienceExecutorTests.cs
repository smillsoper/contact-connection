using System.Net;
using System.Net.Sockets;
using ContactConnection.Infrastructure.ApiExecution;
using Polly.CircuitBreaker;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.ApiExecution;

/// <summary>
/// Covers VendorResilienceExecutor's retry-vs-connection-level-failure classification, circuit
/// breaker behavior, and per-attempt request cloning — the edge-case branches flagged in
/// API_HARDENING_CHECKLIST.md Tier 2 as deserving real test coverage, not just code review.
/// Uses a scripted HttpMessageHandler so no real network/vendor is involved; each test gets its
/// own VendorResilienceExecutor instance so circuit-breaker state never leaks between tests.
/// </summary>
public class VendorResilienceExecutorTests
{
    private static HttpRequestMessage NewRequest(string? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://vendor.example.com/op");
        if (body is not null) req.Content = new StringContent(body);
        return req;
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent("ok") };
    private static HttpResponseMessage ServerError() => new(HttpStatusCode.InternalServerError);
    private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

    private static HttpResponseMessage ThrowConnectionFailure() =>
        throw new HttpRequestException("connection refused", new SocketException());

    private static HttpResponseMessage ThrowGenericHttpFailure() =>
        throw new HttpRequestException("vendor returned garbage"); // no inner SocketException/AuthenticationException

    private static HttpResponseMessage ThrowOwnTimeout() =>
        throw new TaskCanceledException("this attempt's own timeout fired");

    // ── Success / non-retryable outcomes ────────────────────────────────────

    [Fact]
    public async Task SuccessOnFirstAttempt_ReturnsResponse_NoRetry()
    {
        using var handler = new ScriptedHandler([_ => Ok()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        var response = await executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: false, client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FourXxResponse_NeverRetried_EvenWhenRetryAllowed()
    {
        using var handler = new ScriptedHandler([_ => NotFound()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        var response = await executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: true, client);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, handler.CallCount); // a definitive rejection is never retried regardless of the flag
    }

    // ── 5xx (ambiguous) failures ─────────────────────────────────────────────

    [Fact]
    public async Task FiveXxResponse_RetryNotAllowed_ReturnsImmediately()
    {
        using var handler = new ScriptedHandler([_ => ServerError()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        var response = await executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: false, client);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FiveXxResponse_RetryAllowed_ExhaustsMaxAttempts_ReturnsLastResponse()
    {
        using var handler = new ScriptedHandler([_ => ServerError(), _ => ServerError(), _ => ServerError()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        var response = await executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: true, client);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(3, handler.CallCount); // MaxAttempts
    }

    [Fact]
    public async Task FiveXxThenSuccess_RetryAllowed_SucceedsOnSecondAttempt()
    {
        using var handler = new ScriptedHandler([_ => ServerError(), _ => Ok()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        var response = await executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: true, client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    // ── Connection-level vs. generic HttpRequestException classification ────

    [Fact]
    public async Task ConnectionLevelFailure_AlwaysRetried_RegardlessOfFlag()
    {
        using var handler = new ScriptedHandler([_ => ThrowConnectionFailure(), _ => Ok()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        // allowRetryOnAmbiguousFailure is false — retry only happens because this is provably a
        // connection-level failure (request never reached the vendor), not an ambiguous one.
        var response = await executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: false, client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ConnectionLevelFailure_ExhaustsRetries_ThrowsOriginalException()
    {
        using var handler = new ScriptedHandler([_ => ThrowConnectionFailure(), _ => ThrowConnectionFailure(), _ => ThrowConnectionFailure()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: false, client));
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task GenericHttpRequestException_NotConnectionLevel_RetryNotAllowed_ThrowsImmediately()
    {
        using var handler = new ScriptedHandler([_ => ThrowGenericHttpFailure()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        // No inner SocketException/AuthenticationException, so this is treated as ambiguous, not
        // connection-level — with the flag off, it must NOT be retried.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: false, client));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GenericHttpRequestException_NotConnectionLevel_RetryAllowed_Retries()
    {
        using var handler = new ScriptedHandler([_ => ThrowGenericHttpFailure(), _ => Ok()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        var response = await executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: true, client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    // ── This attempt's own timeout vs. the caller cancelling us ─────────────

    [Fact]
    public async Task OwnAttemptTimeout_TreatedAsAmbiguousFailure_RetriedWhenAllowed()
    {
        using var handler = new ScriptedHandler([_ => ThrowOwnTimeout(), _ => Ok()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        var response = await executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: true, client, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task OwnAttemptTimeout_RetryNotAllowed_ThrowsImmediately()
    {
        using var handler = new ScriptedHandler([_ => ThrowOwnTimeout()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: false, client, CancellationToken.None));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesWithoutBeingTreatedAsRetryableFailure()
    {
        using var handler = new ScriptedHandler([_ => Ok()]); // should never be reached
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The caller's own cancellation must NOT be caught by the "this attempt's own timeout"
        // handler (see the `when (!ct.IsCancellationRequested)` filter) — it must propagate
        // straight out, not be reinterpreted as a retryable ambiguous failure.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executor.SendAsync(Guid.NewGuid(), NewRequest(), allowRetryOnAmbiguousFailure: true, client, cts.Token));
        Assert.Equal(0, handler.CallCount);
    }

    // ── Per-attempt request cloning ──────────────────────────────────────────

    [Fact]
    public async Task RetriedRequest_ClonedPerAttempt_BodyAndHeadersSurviveEachAttempt()
    {
        using var handler = new ScriptedHandler([_ => ServerError(), _ => Ok()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();

        var request = NewRequest("hello vendor");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "abc-123");

        var response = await executor.SendAsync(Guid.NewGuid(), request, allowRetryOnAmbiguousFailure: true, client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.ReceivedBodies.Count);
        Assert.All(handler.ReceivedBodies, body => Assert.Equal("hello vendor", body));
        Assert.All(handler.ReceivedRequests, r => Assert.Equal("abc-123", r.Headers.GetValues("X-Correlation-Id").Single()));

        // The original request handed in by the caller must still be usable afterward — proof
        // that only clones were ever sent, never the original instance.
        Assert.Equal("hello vendor", await request.Content!.ReadAsStringAsync());
    }

    // ── Circuit breaker ───────────────────────────────────────────────────────

    [Fact]
    public async Task CircuitOpensAfterFailureThreshold_FailsFastWithoutInvokingHandler()
    {
        // 4 single-attempt failing calls (allowRetryOnAmbiguousFailure: false, so each SendAsync
        // makes exactly one circuit-breaker-tracked attempt) meets MinimumThroughput=4 at a 100%
        // failure ratio — the breaker should open on/after the 4th.
        using var handler = new ScriptedHandler([_ => ServerError(), _ => ServerError(), _ => ServerError(), _ => ServerError()]);
        using var client = new HttpClient(handler);
        var executor = new VendorResilienceExecutor();
        var definitionId = Guid.NewGuid();

        for (var i = 0; i < 4; i++)
        {
            var response = await executor.SendAsync(definitionId, NewRequest(), allowRetryOnAmbiguousFailure: false, client);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        Assert.Equal(4, handler.CallCount);

        // A 5th call must fail fast — BrokenCircuitException, no further handler invocation (the
        // ScriptedHandler has no 5th step queued, so an unexpected invocation would itself throw
        // a different, clearly-wrong exception type and fail this assertion).
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => executor.SendAsync(definitionId, NewRequest(), allowRetryOnAmbiguousFailure: false, client));
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task CircuitBreaker_IsolatedPerDefinitionId()
    {
        using var deadVendorHandler = new ScriptedHandler([_ => ServerError(), _ => ServerError(), _ => ServerError(), _ => ServerError()]);
        using var deadVendorClient = new HttpClient(deadVendorHandler);
        using var healthyVendorHandler = new ScriptedHandler([_ => Ok()]);
        using var healthyVendorClient = new HttpClient(healthyVendorHandler);

        var executor = new VendorResilienceExecutor();
        var deadVendorId = Guid.NewGuid();
        var healthyVendorId = Guid.NewGuid();

        for (var i = 0; i < 4; i++)
            await executor.SendAsync(deadVendorId, NewRequest(), allowRetryOnAmbiguousFailure: false, deadVendorClient);

        // Confirm the dead vendor's circuit actually opened (otherwise this test would prove
        // nothing about isolation).
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => executor.SendAsync(deadVendorId, NewRequest(), allowRetryOnAmbiguousFailure: false, deadVendorClient));

        // A completely different vendor, sharing the same executor instance, must be unaffected.
        var response = await executor.SendAsync(healthyVendorId, NewRequest(), allowRetryOnAmbiguousFailure: false, healthyVendorClient);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, healthyVendorHandler.CallCount);
    }

    /// <summary>Scripted HttpMessageHandler — each SendAsync invocation pops and runs the next
    /// step. Steps that throw propagate exactly as a real HttpClient send would.</summary>
    private class ScriptedHandler(IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> steps) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _steps = new(steps);

        public int CallCount { get; private set; }
        public List<string?> ReceivedBodies { get; } = [];
        public List<HttpRequestMessage> ReceivedRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            ReceivedRequests.Add(request);
            ReceivedBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

            if (_steps.Count == 0)
                throw new InvalidOperationException($"ScriptedHandler received an unscripted call #{CallCount}.");

            return _steps.Dequeue()(request);
        }
    }
}
