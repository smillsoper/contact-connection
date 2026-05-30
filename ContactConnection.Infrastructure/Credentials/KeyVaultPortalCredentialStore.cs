using Azure.Security.KeyVault.Secrets;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Credentials;

internal class KeyVaultPortalCredentialStore : KeyVaultCredentialStoreBase, IPortalCredentialStore
{
    protected override string Prefix => "portal--";

    public KeyVaultPortalCredentialStore(SecretClient client) : base(client) { }

    Task<string?> IPortalCredentialStore.GetAsync(string keyName, CancellationToken ct) => GetAsync(keyName, ct);
    Task IPortalCredentialStore.SetAsync(string keyName, string value, CancellationToken ct) => SetAsync(keyName, value, ct);
    Task IPortalCredentialStore.DeleteAsync(string keyName, CancellationToken ct) => DeleteAsync(keyName, ct);
    Task<IReadOnlyList<CredentialSummary>> IPortalCredentialStore.ListAsync(CancellationToken ct) => ListAsync(ct);
}
