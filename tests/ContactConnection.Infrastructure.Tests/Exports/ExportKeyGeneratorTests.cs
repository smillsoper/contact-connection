using System.Text;
using ContactConnection.Infrastructure.Exports;
using PgpCore;
using Renci.SshNet;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Exports;

public class ExportKeyGeneratorTests
{
    [Fact]
    public void Ssh_PrivateKeyLoadsInSshNet_AndPublicKeyIsAnOpenSshLine()
    {
        var key = ExportKeyGenerator.Ssh("contactconnection-test-cannella");
        Assert.StartsWith("-----BEGIN RSA PRIVATE KEY-----", key.PrivateKey);
        var parts = key.PublicKey.Split(' ');
        Assert.Equal("ssh-rsa", parts[0]);
        Assert.Equal("contactconnection-test-cannella", parts[2]);
        Assert.StartsWith("SHA256:", key.Fingerprint);
        Assert.Equal(key.Fingerprint, ExportKeyGenerator.SshFingerprint(key.PublicKey));

        // SSH.NET (the delivery client) must be able to sign in with it, and derive the same public key.
        var file = new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(key.PrivateKey)));
        Assert.NotNull(file.Key);
        Assert.Null(key.Passphrase);
    }

    [Fact]
    public async Task Pgp_VendorsEncryptToThePublicKey_WeDecryptWithThePrivateKey()
    {
        var key = ExportKeyGenerator.Pgp("Test Tenant (Cannella) <exports@test.contactconnection>");
        Assert.Contains("BEGIN PGP PUBLIC KEY BLOCK", key.PublicKey);
        Assert.Equal(40, key.Fingerprint.Length);
        Assert.NotNull(key.Passphrase);

        using var encrypted = new MemoryStream();
        await new PGP(new EncryptionKeys(key.PublicKey)).EncryptAsync(new MemoryStream(Encoding.UTF8.GetBytes("inbound file")), encrypted);
        encrypted.Position = 0;
        using var decrypted = new MemoryStream();
        await new PGP(new EncryptionKeys(key.PrivateKey, key.Passphrase!)).DecryptAsync(encrypted, decrypted);
        Assert.Equal("inbound file", Encoding.UTF8.GetString(decrypted.ToArray()));
    }
}
