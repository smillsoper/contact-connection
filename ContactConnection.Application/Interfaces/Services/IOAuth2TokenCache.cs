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
}
