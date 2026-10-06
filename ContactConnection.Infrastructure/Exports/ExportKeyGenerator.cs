using System.Security.Cryptography;
using System.Text;
using PgpCore;

namespace ContactConnection.Infrastructure.Exports;

/// <summary>
/// Key pairs for vendors (S180). SSH: RSA-4096, private key as PKCS#1 PEM (what SSH.NET reads), public key as an OpenSSH
/// <c>ssh-rsa</c> line, fingerprint as OpenSSH shows it (<c>SHA256:…</c>). PGP: PgpCore's RSA key pair protected by a random
/// passphrase; the fingerprint is the primary key's (hex).
/// </summary>
public static class ExportKeyGenerator
{
    public sealed record GeneratedKey(string PublicKey, string PrivateKey, string Fingerprint, string? Passphrase);

    public static GeneratedKey Ssh(string comment)
    {
        using var rsa = RSA.Create(4096);
        var p = rsa.ExportParameters(false);
        var blob = SshBlob(p);
        var publicKey = $"ssh-rsa {Convert.ToBase64String(blob)} {Comment(comment)}";
        return new(publicKey, rsa.ExportRSAPrivateKeyPem(), SshFingerprint(blob), null);
    }

    public static GeneratedKey Pgp(string identity)
    {
        var passphrase = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var pub = new MemoryStream();
        using var priv = new MemoryStream();
        new PGP().GenerateKey(pub, priv, identity, passphrase);
        var publicKey = Encoding.UTF8.GetString(pub.ToArray());
        var fingerprint = PgpFingerprint(publicKey);
        return new(publicKey, Encoding.UTF8.GetString(priv.ToArray()), fingerprint, passphrase);
    }

    /// <summary>The primary key's fingerprint (hex) of an armored PGP public key.</summary>
    public static string PgpFingerprint(string armoredPublicKey)
    {
        using var input = Org.BouncyCastle.Bcpg.OpenPgp.PgpUtilities.GetDecoderStream(new MemoryStream(Encoding.UTF8.GetBytes(armoredPublicKey)));
        var bundle = new Org.BouncyCastle.Bcpg.OpenPgp.PgpPublicKeyRingBundle(input);
        var ring = bundle.GetKeyRings().First();
        return Convert.ToHexString(ring.GetPublicKey().GetFingerprint());
    }

    /// <summary>The OpenSSH "SHA256:…" fingerprint of an <c>ssh-rsa …</c> public key line.</summary>
    public static string SshFingerprint(string publicKeyLine) =>
        SshFingerprint(Convert.FromBase64String(publicKeyLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]));

    private static string SshFingerprint(byte[] blob) => "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');

    /// <summary>RFC 4253 public key blob: string "ssh-rsa", mpint e, mpint n.</summary>
    private static byte[] SshBlob(RSAParameters p)
    {
        using var ms = new MemoryStream();
        WriteString(ms, Encoding.ASCII.GetBytes("ssh-rsa"));
        WriteString(ms, Mpint(p.Exponent!));
        WriteString(ms, Mpint(p.Modulus!));
        return ms.ToArray();
    }

    private static byte[] Mpint(byte[] unsignedBigEndian)
    {
        var i = 0;
        while (i < unsignedBigEndian.Length - 1 && unsignedBigEndian[i] == 0) i++;
        var trimmed = unsignedBigEndian[i..];
        // A leading 1 bit would read as negative — prefix a zero byte.
        return (trimmed[0] & 0x80) != 0 ? [0, .. trimmed] : trimmed;
    }

    private static void WriteString(Stream s, byte[] data)
    {
        var len = BitConverter.GetBytes(data.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(len);
        s.Write(len);
        s.Write(data);
    }

    private static string Comment(string s) => new(s.Where(ch => ch > ' ' && ch < 127).ToArray());
}
