namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Per-vendor (per API Definition) circuit breaker + retry wrapper around an outbound HTTP call.
/// Circuit state is keyed by definitionId so one dead vendor can't trip the breaker for every
/// other vendor sharing the same HttpClient — see API_HARDENING_CHECKLIST.md Tier 1.
/// </summary>
public interface IVendorResilienceExecutor
{
    /// <summary>
    /// Sends request through definitionId's circuit breaker, retrying on transient failures —
    /// always for a pure connection-level failure (the request provably never reached the
    /// vendor), and additionally for timeouts/5xx when allowRetryOnAmbiguousFailure is true. A
    /// non-retryable outcome (4xx, or an ambiguous failure when retry isn't allowed) is returned/
    /// thrown as-is on the first attempt. When the circuit is open, fails immediately without
    /// attempting the call at all.
    ///
    /// request is cloned per attempt (never sent directly), so callers build it once and it's
    /// left in a valid, re-readable state regardless of how many attempts this makes.
    /// </summary>
    Task<HttpResponseMessage> SendAsync(
        Guid definitionId,
        HttpRequestMessage request,
        bool allowRetryOnAmbiguousFailure,
        HttpClient httpClient,
        CancellationToken ct = default);
}
