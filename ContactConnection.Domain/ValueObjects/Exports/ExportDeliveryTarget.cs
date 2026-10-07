namespace ContactConnection.Domain.ValueObjects.Exports;

/// <summary>
/// Where an export's files go (S180, Export Worker session 2). Secrets are never stored here — only the <b>names</b> of
/// tenant credentials (Key Vault) holding the password, private key or zip password. Servers are pinned: an SFTP target
/// carries the host-key fingerprint and an FTPS target may carry the certificate fingerprint, both taken from Test
/// connection, so a file can't be handed to an impostor server.
/// </summary>
public sealed record ExportDeliveryTarget
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Type { get; init; } = ExportDeliveryType.Sftp;
    public bool Enabled { get; init; } = true;

    // ── SFTP / FTPS ────────────────────────────────────────────────────────────
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Username { get; init; }
    /// <summary>Tenant credential name holding the password.</summary>
    public string? PasswordCredential { get; init; }
    /// <summary>SFTP: tenant credential name holding an OpenSSH / PEM private key (used instead of, or with, a password).</summary>
    public string? PrivateKeyCredential { get; init; }
    /// <summary>SFTP: the server's SHA-256 host-key fingerprint (base64, as Test connection shows it). Required to deliver.</summary>
    public string? HostKeyFingerprint { get; init; }
    /// <summary>FTPS: implicit TLS (port 990) instead of explicit (AUTH TLS on 21).</summary>
    public bool FtpsImplicit { get; init; }
    /// <summary>FTPS: SHA-256 certificate fingerprint — when set, only that certificate is accepted (self-signed included);
    /// when blank, the certificate must be valid for the host.</summary>
    public string? CertificateFingerprint { get; init; }
    /// <summary>Remote folder, e.g. <c>/incoming</c>. Blank = the login folder.</summary>
    public string? RemoteDirectory { get; init; }

    // ── Email ──────────────────────────────────────────────────────────────────
    public List<string> EmailTo { get; init; } = [];
    /// <summary>Liquid, e.g. <c>{{ export.name }} — {{ file_name }}</c>.</summary>
    public string? EmailSubject { get; init; }

    // ── Encryption (applied to this target's copy only) ────────────────────────
    /// <summary><c>none</c>, <c>pgp</c> (to <see cref="PgpPublicKey"/>; adds .pgp) or <c>zip</c> (AES-256 zip; adds .zip).</summary>
    public string Encryption { get; init; } = ExportEncryption.None;
    /// <summary>The recipient's ASCII-armored PGP public key (public — not a secret).</summary>
    public string? PgpPublicKey { get; init; }
    /// <summary>Tenant credential name holding the zip password.</summary>
    public string? ZipPasswordCredential { get; init; }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Every delivery target needs a name.";
        if (!ExportDeliveryType.IsValid(Type)) return $"{Name}: unknown type '{Type}'.";
        if (Type is ExportDeliveryType.Sftp or ExportDeliveryType.Ftps)
        {
            if (string.IsNullOrWhiteSpace(Host)) return $"{Name}: a host is required.";
            if (Port is < 1 or > 65535) return $"{Name}: the port must be 1–65535.";
            if (string.IsNullOrWhiteSpace(Username)) return $"{Name}: a user name is required.";
            if (string.IsNullOrWhiteSpace(PasswordCredential) && string.IsNullOrWhiteSpace(PrivateKeyCredential))
                return $"{Name}: choose the stored password{(Type == ExportDeliveryType.Sftp ? " or private key" : "")}.";
            if (Type == ExportDeliveryType.Ftps && !string.IsNullOrWhiteSpace(PrivateKeyCredential) && string.IsNullOrWhiteSpace(PasswordCredential))
                return $"{Name}: FTPS signs in with a password.";
        }
        if (Type == ExportDeliveryType.Email)
        {
            if (EmailTo.Count == 0) return $"{Name}: add at least one email address.";
            if (EmailTo.Any(e => !e.Contains('@'))) return $"{Name}: '{EmailTo.First(e => !e.Contains('@'))}' isn't an email address.";
        }
        if (!ExportEncryption.IsValid(Encryption)) return $"{Name}: unknown encryption '{Encryption}'.";
        if (Encryption == ExportEncryption.Pgp && string.IsNullOrWhiteSpace(PgpPublicKey)) return $"{Name}: paste the recipient's PGP public key.";
        if (Encryption == ExportEncryption.Zip && string.IsNullOrWhiteSpace(ZipPasswordCredential)) return $"{Name}: choose the stored zip password.";
        return null;
    }

    /// <summary>Why this target can't carry card data (S182), or null: FTPS (SFTP is disabled by the PCI scans the platform
    /// follows) with PGP to the recipient's key — never email, never a password zip.</summary>
    public string? CardDataProblem() =>
        Type != ExportDeliveryType.Ftps ? $"{Name}: a card-data file can only go by FTPS."
        : Encryption != ExportEncryption.Pgp ? $"{Name}: a card-data file must be PGP-encrypted to the recipient's key."
        : string.IsNullOrWhiteSpace(PgpPublicKey) ? $"{Name}: paste the recipient's PGP public key."
        : null;

    public int DefaultPort => Type == ExportDeliveryType.Sftp ? 22 : FtpsImplicit ? 990 : 21;
}

public static class ExportDeliveryType
{
    public const string Sftp = "sftp";
    public const string Ftps = "ftps";
    public const string Email = "email";
    public static bool IsValid(string? v) => v is Sftp or Ftps or Email;
}

public static class ExportEncryption
{
    public const string None = "none";
    public const string Pgp = "pgp";
    public const string Zip = "zip";
    public static bool IsValid(string? v) => v is None or Pgp or Zip;
}
