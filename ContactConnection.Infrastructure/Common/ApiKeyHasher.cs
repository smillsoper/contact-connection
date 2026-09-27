using System.Security.Cryptography;
using System.Text;

namespace ContactConnection.Infrastructure.Common;

/// <summary>
/// Issues and verifies API keys for external callers of our endpoints (e.g. a routing platform's
/// availability / routing requests). Keys are 32 random bytes, base64url, prefixed "ccrk_"; only the
/// SHA-256 hash is stored (plus a short display prefix) — the plaintext is shown exactly once.
/// SHA-256 (not a slow password hash) is appropriate: the key is high-entropy random, not a
/// human-chosen secret, and lookups happen per request.
/// </summary>
public static class ApiKeyHasher
{
    public const string KeyPrefix = "ccrk_";

    public static (string PlainText, string Hash, string DisplayPrefix) Issue()
    {
        var plain = KeyPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (plain, Hash(plain), plain[..12]);
    }

    public static string Hash(string plainText)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plainText)));
}
