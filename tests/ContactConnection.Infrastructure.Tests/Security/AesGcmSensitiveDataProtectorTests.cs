using System.Security.Cryptography;
using ContactConnection.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Security;

/// <summary>AES-256-GCM sensitive-data protector (tf_secure_collect → call_records.sensitive_data).</summary>
public class AesGcmSensitiveDataProtectorTests
{
    private static AesGcmSensitiveDataProtector Build(string? key)
    {
        var settings = new Dictionary<string, string?>();
        if (key is not null) settings["SensitiveData:MasterKey"] = key;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new AesGcmSensitiveDataProtector(config, NullLogger<AesGcmSensitiveDataProtector>.Instance);
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void RoundTrips_Utf8Payload()
    {
        var p = Build(NewKey());
        Assert.True(p.IsConfigured);

        const string plain = """{"pan":"4242424242424242","expiry":"1230","cvv":"123"}""";
        var token = p.Protect(plain);

        Assert.NotEqual(plain, token);
        Assert.Equal(plain, p.Unprotect(token));
    }

    [Fact]
    public void Protect_IsNonDeterministic_FreshNoncePerCall()
    {
        var p = Build(NewKey());
        Assert.NotEqual(p.Protect("same"), p.Protect("same"));
    }

    [Fact]
    public void Unprotect_TamperedToken_Throws()
    {
        var p = Build(NewKey());
        var token = Convert.FromBase64String(p.Protect("secret"));
        token[^1] ^= 0xFF; // flip a ciphertext bit
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(Convert.ToBase64String(token)));
    }

    [Fact]
    public void Unprotect_WrongKey_Throws()
    {
        var token = Build(NewKey()).Protect("secret");
        Assert.ThrowsAny<CryptographicException>(() => Build(NewKey()).Unprotect(token));
    }

    [Theory]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")]            // valid base64, far too short for version+nonce+tag
    public void Unprotect_MalformedToken_Throws(string token) =>
        Assert.ThrowsAny<CryptographicException>(() => Build(NewKey()).Unprotect(token));

    [Fact]
    public void NotConfigured_WhenKeyMissing_Or_WrongLength_Or_NotBase64()
    {
        Assert.False(Build(null).IsConfigured);
        Assert.False(Build("").IsConfigured);
        Assert.False(Build(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))).IsConfigured); // 128-bit, need 256
        Assert.False(Build("%%%not base64%%%").IsConfigured);
    }

    [Fact]
    public void Protect_WhenNotConfigured_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Build(null).Protect("x"));
}
