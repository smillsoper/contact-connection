using System.Security.Cryptography;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Security;

/// <summary>
/// AES-256-GCM implementation of <see cref="ISensitiveDataProtector"/>. Master key read once at
/// construction from <c>SensitiveData:MasterKey</c> — a base64-encoded 32-byte key, held in Key
/// Vault (prod) or user-secrets (local), never in a committed file. When the key is absent or
/// malformed the protector is inert (<see cref="IsConfigured"/> == false) and every
/// encrypt/decrypt throws with a clear message rather than silently using a weak default.
///
/// Token layout (all concatenated, then base64):
///   [1 byte version = 0x01][12-byte nonce][16-byte GCM tag][ciphertext]
/// </summary>
public sealed class AesGcmSensitiveDataProtector : ISensitiveDataProtector
{
    private const byte Version = 0x01;
    private const int KeyBytes   = 32; // AES-256
    private const int NonceBytes = 12; // GCM standard
    private const int TagBytes   = 16;

    private readonly byte[]? _key;

    public AesGcmSensitiveDataProtector(IConfiguration config, ILogger<AesGcmSensitiveDataProtector> logger)
    {
        var raw = config["SensitiveData:MasterKey"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning(
                "SensitiveData:MasterKey is not configured — tf_secure_collect captures cannot be stored " +
                "until it is set (base64 of 32 random bytes, in Key Vault / user-secrets).");
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(raw.Trim());
            if (bytes.Length != KeyBytes)
            {
                logger.LogError(
                    "SensitiveData:MasterKey decoded to {Len} bytes — expected {Expected} (AES-256). Protector disabled.",
                    bytes.Length, KeyBytes);
                return;
            }
            _key = bytes;
        }
        catch (FormatException)
        {
            logger.LogError("SensitiveData:MasterKey is not valid base64. Protector disabled.");
        }
    }

    public bool IsConfigured => _key is not null;

    public string Protect(string plaintext)
    {
        var key = RequireKey();
        ArgumentNullException.ThrowIfNull(plaintext);

        var pl=Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[pl.Length];
        var tag = new byte[TagBytes];

        using var gcm = new AesGcm(key, TagBytes);
        gcm.Encrypt(nonce, pl,cipher, tag);

        var token = new byte[1 + NonceBytes + TagBytes + cipher.Length];
        token[0] = Version;
        Buffer.BlockCopy(nonce, 0, token, 1, NonceBytes);
        Buffer.BlockCopy(tag, 0, token, 1 + NonceBytes, TagBytes);
        Buffer.BlockCopy(cipher, 0, token, 1 + NonceBytes + TagBytes, cipher.Length);
        return Convert.ToBase64String(token);
    }

    public string Unprotect(string token)
    {
        var key = RequireKey();
        if (string.IsNullOrEmpty(token)) throw new ArgumentException("Token is empty.", nameof(token));

        byte[] raw;
        try { raw = Convert.FromBase64String(token); }
        catch (FormatException) { throw new CryptographicException("Sensitive-data token is not valid base64."); }

        if (raw.Length < 1 + NonceBytes + TagBytes || raw[0] != Version)
            throw new CryptographicException("Sensitive-data token is malformed or an unknown version.");

        var nonce  = raw.AsSpan(1, NonceBytes);
        var tag    = raw.AsSpan(1 + NonceBytes, TagBytes);
        var cipher = raw.AsSpan(1 + NonceBytes + TagBytes);
        var plain  = new byte[cipher.Length];

        using var gcm = new AesGcm(key, TagBytes);
        gcm.Decrypt(nonce, cipher, tag, plain); // throws CryptographicException on tamper / wrong key
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] RequireKey() => _key
        ?? throw new InvalidOperationException(
            "SensitiveData:MasterKey is not configured — cannot encrypt/decrypt sensitive data.");
}
