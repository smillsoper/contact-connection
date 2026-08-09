using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using ContactConnection.Application.Interfaces.Services;
using Polly;
using Polly.CircuitBreaker;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// Default IVendorResilienceExecutor. Each definitionId gets its own long-lived circuit breaker
/// pipeline (cached for the process lifetime — this type is registered as a singleton), separate
/// from every other vendor's. Retry is deliberately NOT baked into the cached pipeline: whether a
/// given call is safe to retry depends on the specific endpoint/HTTP method of THIS call, not the
/// vendor as a whole, so it's applied as a manual loop wrapping the circuit-breaker-protected send.
/// </summary>
internal class VendorResilienceExecutor : IVendorResilienceExecutor
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan[] BackoffDelays = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500)];

    private readonly ConcurrentDictionary<Guid, ResiliencePipeline<HttpResponseMessage>> _circuitBreakers = new();

    public async Task<HttpResponseMessage> SendAsync(
        Guid definitionId,
        HttpRequestMessage request,
        bool allowRetryOnAmbiguousFailure,
        HttpClient httpClient,
        CancellationToken ct = default)
    {
        var circuitBreaker = _circuitBreakers.GetOrAdd(definitionId, static _ => BuildCircuitBreakerPipeline());

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var isLastAttempt = attempt == MaxAttempts;
            using var attemptRequest = await CloneRequestAsync(request, ct);

            HttpResponseMessage? response = null;
            Exception? failure = null;
            var connectionLevelFailure = false;

            try
            {
                response = await circuitBreaker.ExecuteAsync(
                    innerCt => new ValueTask<HttpResponseMessage>(httpClient.SendAsync(attemptRequest, innerCt)),
                    ct);
            }
            catch (BrokenCircuitException)
            {
                // Circuit is open — the vendor has been failing repeatedly. Fail fast, don't
                // waste an attempt (or the caller's timeout budget) probing a known-down vendor.
                throw;
            }
            catch (HttpRequestException ex)
            {
                failure = ex;
                // Set when the failure happened before an HTTP response could even be framed
                // (DNS, TCP connect, TLS handshake) — the request provably never reached the
                // vendor, so retrying is always safe here regardless of HTTP method.
                connectionLevelFailure = ex.InnerException is SocketException or AuthenticationException;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                failure = ex; // this request's own timeout fired — not the caller cancelling us
            }

            if (failure is null && response is not null)
            {
                if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
                    return response; // success, or a definitive rejection (4xx) — never retry a 4xx

                if (!allowRetryOnAmbiguousFailure || isLastAttempt) return response; // 5xx, out of retries or not allowed
                response.Dispose();
            }
            else if (failure is not null)
            {
                if (!(connectionLevelFailure || allowRetryOnAmbiguousFailure) || isLastAttempt) throw failure;
            }

            await Task.Delay(BackoffDelays[Math.Min(attempt - 1, BackoffDelays.Length - 1)], ct);
        }

        // Unreachable: the loop above always returns or throws by MaxAttempts.
        throw new InvalidOperationException("VendorResilienceExecutor retry loop exhausted without a result.");
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildCircuitBreakerPipeline() =>
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                // Needs at least 4 calls in a 30s window before the breaker will even consider
                // opening — a single failed call, or a quiet vendor with only 1-2 calls, never
                // trips it. Trips at a 50% failure ratio once that minimum is met.
                FailureRatio = 0.5,
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => (int)r.StatusCode >= (int)HttpStatusCode.InternalServerError),
            })
            .Build();

    /// <summary>HttpRequestMessage can only be sent once; clone it per attempt so callers build
    /// it a single time and this executor can retry freely. Buffered content only (StringContent/
    /// FormUrlEncodedContent/ByteArrayContent) — matches what ApiDefinitionExecutor and
    /// ApiEndpointTestHelper actually construct; a streamed-content request would need different
    /// handling, but neither caller uses one.</summary>
    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync(ct);
            var content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = content;
        }

        return clone;
    }
}
