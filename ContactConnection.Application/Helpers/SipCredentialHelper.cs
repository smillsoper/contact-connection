using System.Security.Cryptography;
using System.Text;

namespace ContactConnection.Application.Helpers;

public static class SipCredentialHelper
{
    /// <summary>
    /// Generates a 16-character random SIP password (96 bits of entropy).
    /// Returned once at login time; only the a1hash is persisted.
    /// </summary>
    public static string GeneratePassword()
    {
        // 12 bytes → exactly 16 base64 chars, no padding needed
        var bytes = RandomNumberGenerator.GetBytes(12);
        return Convert.ToBase64String(bytes)
            .Replace('+', 'A')
            .Replace('/', 'B');
    }

    /// <summary>
    /// Computes the RFC 2617 HA1 = MD5("{extension}:{realm}:{password}").
    /// FreeSWITCH accepts this pre-computed hash in the directory XML, avoiding
    /// plaintext password storage.
    /// </summary>
    public static string ComputeA1Hash(string extension, string realm, string password)
    {
        var input = $"{extension}:{realm}:{password}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
