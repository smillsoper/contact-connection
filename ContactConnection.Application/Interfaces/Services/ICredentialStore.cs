namespace ContactConnection.Application.Interfaces.Services;

public record CredentialSummary(string KeyName, DateTimeOffset? UpdatedOn);

public interface IPortalCredentialStore
{
    Task<string?> GetAsync(string keyName, CancellationToken ct = default);
    Task SetAsync(string keyName, string value, CancellationToken ct = default);
    Task DeleteAsync(string keyName, CancellationToken ct = default);
    Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken ct = default);
}

public interface ITenantCredentialStore
{
    Task<string?> GetAsync(string keyName, CancellationToken ct = default);
    Task SetAsync(string keyName, string value, CancellationToken ct = default);
    Task DeleteAsync(string keyName, CancellationToken ct = default);
    Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken ct = default);
}
