using Azure;
using Azure.Security.KeyVault.Secrets;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Credentials;

// Key Vault secret names: alphanumeric + hyphens only, max 127 chars.
// Portal secrets:  portal--{key}
// Tenant secrets:  tenant--{subdomain}--{key}
internal abstract class KeyVaultCredentialStoreBase
{
    private readonly SecretClient _client;
    protected abstract string Prefix { get; }

    protected KeyVaultCredentialStoreBase(SecretClient client) => _client = client;

    protected static string Sanitize(string key) =>
        new string(key.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');

    protected string VaultName(string keyName) =>
        $"{Prefix}{Sanitize(keyName)}"[..Math.Min($"{Prefix}{Sanitize(keyName)}".Length, 127)];

    protected string StripPrefix(string vaultName) =>
        vaultName.StartsWith(Prefix) ? vaultName[Prefix.Length..] : vaultName;

    public async Task<string?> GetAsync(string keyName, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetSecretAsync(VaultName(keyName), cancellationToken: ct);
            return response.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SetAsync(string keyName, string value, CancellationToken ct = default)
    {
        await _client.SetSecretAsync(VaultName(keyName), value, ct);
    }

    public async Task DeleteAsync(string keyName, CancellationToken ct = default)
    {
        try
        {
            await _client.StartDeleteSecretAsync(VaultName(keyName), ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // already gone — treat as success
        }
    }

    public async Task<IReadOnlyList<CredentialSummary>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<CredentialSummary>();
        await foreach (var props in _client.GetPropertiesOfSecretsAsync(ct))
        {
            if (!props.Name.StartsWith(Prefix) || props.Enabled == false) continue;
            results.Add(new CredentialSummary(
                StripPrefix(props.Name),
                props.UpdatedOn));
        }
        return results.OrderBy(r => r.KeyName).ToList();
    }
}
