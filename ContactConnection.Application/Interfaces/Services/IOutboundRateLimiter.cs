namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Outbound-call throttling keyed per API Definition — protects a vendor (and, for a Portal
/// definition shared by multiple tenants via a platform-default credential, the shared quota
/// itself) from a runaway or looping flow. See API_HARDENING_CHECKLIST.md Tier 2.
/// </summary>
public interface IOutboundRateLimiter
{
    /// <summary>
    /// Registers one call against definitionId's per-minute budget and returns whether it's
    /// allowed. limitPerMinute is the definition's own configured limit — null or non-positive
    /// means unlimited, so this always returns true without touching the store (an unconfigured
    /// definition's behavior never changes). A denied call has already been counted against the
    /// window like an allowed one would be — callers should not call this again for the same
    /// logical request.
    /// </summary>
    Task<RateLimitDecision> TryAcquireAsync(Guid definitionId, int? limitPerMinute, CancellationToken ct = default);
}

/// <summary>Allowed, or denied with how many seconds remain until the current window rolls over
/// (for a caller that wants to surface a "retry after" hint).</summary>
public record RateLimitDecision(bool Allowed, int RetryAfterSeconds = 0)
{
    public static readonly RateLimitDecision Allow = new(true);
}

/// <summary>Thrown by callers that enforce a denied RateLimitDecision as a hard failure (matching
/// how a tripped circuit breaker surfaces as BrokenCircuitException) — caught generically by
/// ApiDefinitionExecutor/ApiEndpointTestHelper's existing catch-all and normalized into their
/// uniform Error result, same as any other outbound failure.</summary>
public class RateLimitExceededException(Guid definitionId, int limitPerMinute, int retryAfterSeconds)
    : Exception($"Rate limit exceeded ({limitPerMinute}/min) for this API definition. Retry after {retryAfterSeconds}s.")
{
    public Guid DefinitionId { get; } = definitionId;
    public int LimitPerMinute { get; } = limitPerMinute;
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
