namespace ContactConnection.Application.Interfaces.Services;

public record CredentialSummary(string KeyName, DateTimeOffset? UpdatedOn, DateTimeOffset? ExpiresOn);

public interface IPortalCredentialStore
{
    Task<string?> GetAsync(string keyName, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="expiresOn"/> is optional — null means "no known expiry". Every SetAsync call
    /// creates a new Key Vault secret version, so an existing expiry does NOT carry over on rotation;
    /// callers that want to preserve it must pass it again explicitly.
    /// </summary>
    Task SetAsync(string keyName, string value, DateTimeOffset? expiresOn = null, CancellationToken ct = default);
    Task DeleteAsync(string keyName, CancellationToken ct = default);
    Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken ct = default);
}

public interface ITenantCredentialStore
{
    Task<string?> GetAsync(string keyName, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="expiresOn"/> is optional — null means "no known expiry". Every SetAsync call
    /// creates a new Key Vault secret version, so an existing expiry does NOT carry over on rotation;
    /// callers that want to preserve it must pass it again explicitly.
    /// </summary>
    Task SetAsync(string keyName, string value, DateTimeOffset? expiresOn = null, CancellationToken ct = default);
    Task DeleteAsync(string keyName, CancellationToken ct = default);
    Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads a tenant credential for an explicitly-named tenant, bypassing the ambient
    /// (HTTP-request-scoped) TenantContext. Needed by the telephony flow engine, which runs
    /// from a background service with no HTTP request and thus no resolved TenantContext.
    /// </summary>
    Task<string?> GetForTenantAsync(string tenantSubdomain, string keyName, CancellationToken ct = default);
}
