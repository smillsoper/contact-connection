using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Credentials;

// Registered when KeyVault:VaultUri is not configured — keeps DI intact so
// credential endpoints can start up. All write operations fail with a clear message.
internal class NullCredentialStore : IPortalCredentialStore, ITenantCredentialStore
{
    private static readonly Task<string?> NullString = Task.FromResult<string?>(null);

    private static readonly IReadOnlyList<CredentialSummary> EmptyList =
        Array.Empty<CredentialSummary>();

    Task<string?> IPortalCredentialStore.GetAsync(string keyName, CancellationToken ct) => NullString;
    Task<string?> ITenantCredentialStore.GetAsync(string keyName, CancellationToken ct) => NullString;
    Task<string?> ITenantCredentialStore.GetForTenantAsync(string tenantSubdomain, string keyName, CancellationToken ct) => NullString;

    Task<IReadOnlyList<CredentialSummary>> IPortalCredentialStore.ListAsync(CancellationToken ct) =>
        Task.FromResult(EmptyList);

    Task<IReadOnlyList<CredentialSummary>> ITenantCredentialStore.ListAsync(CancellationToken ct) =>
        Task.FromResult(EmptyList);

    Task IPortalCredentialStore.SetAsync(string keyName, string value, CancellationToken ct) => Fail();
    Task IPortalCredentialStore.DeleteAsync(string keyName, CancellationToken ct) => Fail();
    Task ITenantCredentialStore.SetAsync(string keyName, string value, CancellationToken ct) => Fail();
    Task ITenantCredentialStore.DeleteAsync(string keyName, CancellationToken ct) => Fail();

    private static Task Fail() =>
        Task.FromException(new InvalidOperationException(
            "Key Vault is not configured. Set KeyVault:VaultUri in application secrets."));
}
