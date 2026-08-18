namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Caches OAuth2 client_credentials access tokens so ApiDefinitionExecutor doesn't repeat the
/// full token exchange (a round trip to the vendor's token endpoint, on top of the credential
/// store lookups it takes to get there) on every API call that uses oauth2 auth. Keyed by a hash
/// of the token request's identity — see ApiDefinitionExecutor.BuildOAuth2CacheKey — so distinct
/// tenants and distinct credential values never collide or share a cached token.
/// </summary>
public interface IOAuth2TokenCache
{
    Task<string?> GetAsync(string cacheKey, CancellationToken ct = default);

    /// <summary>No-ops if ttl is zero or negative — a token that's already expired (or about to,
    /// after the caller's own safety buffer) isn't worth caching.</summary>
    Task SetAsync(string cacheKey, string token, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Cache-stampede-protected get-or-create — see API_HARDENING_CHECKLIST.md Tier 2. On a cache
    /// hit, returns immediately without calling <paramref name="exchange"/>. On a miss, only one
    /// caller (across threads and, via a short-lived distributed lock, process instances) actually
    /// runs the exchange; every other concurrent caller for the same key waits briefly for that
    /// result instead of also hitting the vendor's token endpoint. If the lock holder doesn't
    /// finish within the wait budget (stuck, crashed, or just slow), waiters fall back to running
    /// <paramref name="exchange"/> themselves rather than blocking indefinitely — bounded, but not
    /// perfectly de-duplicated in that edge case. <paramref name="exchange"/> returning null means
    /// "no token obtained" (e.g. malformed vendor response); nothing is cached and this returns
    /// null too, matching the existing "proceed unauthenticated" behavior on exchange failure.
    /// </summary>
    Task<string?> GetOrCreateAsync(
        string cacheKey,
        Func<CancellationToken, Task<(string Token, TimeSpan Ttl)?>> exchange,
        CancellationToken ct = default);
}
