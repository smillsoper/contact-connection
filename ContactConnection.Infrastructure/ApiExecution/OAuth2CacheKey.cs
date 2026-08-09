using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// Shared oauth2 token-cache key/TTL derivation — used by both ApiDefinitionExecutor (flow
/// engine node handlers) and ApiEndpointTestHelper (the Api project's live address
/// validation/ZIP lookup/autocomplete resolution in FlowSessionsEndpoints; NOT the admin/portal
/// "Test" button call sites, which intentionally stay uncached so a test click always reflects
/// the current live config). Centralized so both call sites derive the *same* key for the same
/// credentials and therefore actually share a cached token instead of quietly missing each
/// other's cache entries.
/// </summary>
public static class OAuth2CacheKey
{
    // Refresh this many seconds before actual expiry, so a cache hit never hands out a token
    // that dies mid-flight on the caller's request.
    private const int ExpiryBufferSeconds = 30;

    // Used when the token response doesn't include the configured expiresInField (or it's
    // unparseable) — a conservative lifetime rather than caching an unknown-duration token
    // indefinitely.
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    /// <summary>Same resolved client credentials + same token endpoint = same token, so this
    /// naturally scopes per-tenant (different tenants' credential VALUES differ even when the
    /// credential key NAME is identical) without needing to thread a tenant/portal identifier
    /// through either caller. Hashing keeps the client secret itself out of the Redis key.</summary>
    public static string Build(
        string tokenUrl, string method, string placement, string clientId, string clientSecret,
        string scopes, string tokenField)
    {
        var identity = string.Join('|', tokenUrl, method, placement, clientId, clientSecret, scopes, tokenField);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash);
    }

    public static TimeSpan ComputeTtl(JsonElement tokenResponseRoot, string expiresInField)
    {
        if (tokenResponseRoot.TryGetProperty(expiresInField, out var expiresProp) &&
            expiresProp.TryGetInt32(out var expiresInSeconds) && expiresInSeconds > 0)
        {
            return TimeSpan.FromSeconds(Math.Max(expiresInSeconds - ExpiryBufferSeconds, 30));
        }
        return DefaultTtl;
    }
}
