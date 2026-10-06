namespace ContactConnection.Domain.Entities;

/// <summary>
/// A key pair ContactConnection generated for a vendor (S180, Export Worker — key management):
///
///   ssh — we sign in to the vendor's SFTP server with it: the vendor authorizes the <see cref="PublicKey"/>, the private
///         key stays with us (an SFTP delivery target names <see cref="PrivateKeyCredential"/>).
///   pgp — vendors encrypt files TO us with the <see cref="PublicKey"/>; we decrypt with the private key (inbound files —
///         ingestion is a later build, the key is ready now).
///
/// Only the public half and its fingerprint live here. The private key (and a PGP key's passphrase) were written straight
/// to the tenant credential store (Key Vault) when generated and are never shown or returned.
/// </summary>
public class ExportKey
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Type { get; private set; } = ExportKeyType.Ssh;
    public string PublicKey { get; private set; } = string.Empty;
    public string Fingerprint { get; private set; } = string.Empty;
    public string PrivateKeyCredential { get; private set; } = string.Empty;
    public string? PassphraseCredential { get; private set; }
    public string CreatedByName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedByName { get; private set; }

    private ExportKey() { }

    public static ExportKey Create(Guid tenantId, string name, string type, string publicKey, string fingerprint,
        string privateKeyCredential, string? passphraseCredential, string createdByName) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Name = name.Trim(), Type = type, PublicKey = publicKey, Fingerprint = fingerprint,
        PrivateKeyCredential = privateKeyCredential, PassphraseCredential = passphraseCredential,
        CreatedByName = createdByName, CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>The private key has been deleted from the credential store; the record stays for history.</summary>
    public void Revoke(string byName)
    {
        RevokedAt ??= DateTimeOffset.UtcNow;
        RevokedByName ??= byName;
    }
}

public static class ExportKeyType
{
    public const string Ssh = "ssh";
    public const string Pgp = "pgp";
    public static bool IsValid(string? v) => v is Ssh or Pgp;
}
