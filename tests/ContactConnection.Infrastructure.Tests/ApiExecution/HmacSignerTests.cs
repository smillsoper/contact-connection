using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ContactConnection.Infrastructure.ApiExecution;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.ApiExecution;

/// <summary>Covers HmacSigner — the pure signing helper behind the "hmac" auth type.
/// See API_HARDENING_CHECKLIST.md Tier 2.</summary>
public class HmacSignerTests
{
    [Fact]
    public void ComputeSignatureHeaderValue_NoTimestamp_ReturnsBareHexHmac()
    {
        var value = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "the-payload", includeTimestamp: false);

        var expected = Convert.ToHexStringLower(new HMACSHA256(Encoding.UTF8.GetBytes("sekret"))
            .ComputeHash(Encoding.UTF8.GetBytes("the-payload")));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void ComputeSignatureHeaderValue_WithTimestamp_UsesStripeStyleFormat_AndSignsTimestampDotPayload()
    {
        var value = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "the-payload", includeTimestamp: true);

        var match = Regex.Match(value, @"^t=(\d+),v1=([0-9a-f]+)$");
        Assert.True(match.Success, $"unexpected format: {value}");

        var timestamp = match.Groups[1].Value;
        var expectedHash = Convert.ToHexStringLower(new HMACSHA256(Encoding.UTF8.GetBytes("sekret"))
            .ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.the-payload")));
        Assert.Equal(expectedHash, match.Groups[2].Value);

        // The embedded timestamp should be "now", within a generous tolerance for test execution time.
        var embedded = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp));
        Assert.True((DateTimeOffset.UtcNow - embedded).Duration() < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ComputeSignatureHeaderValue_NullPayload_TreatedAsEmptyString()
    {
        var value = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", null, includeTimestamp: false);
        var expected = Convert.ToHexStringLower(new HMACSHA256(Encoding.UTF8.GetBytes("sekret"))
            .ComputeHash(Encoding.UTF8.GetBytes("")));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("SHA256")]
    [InlineData("sha256")]
    [InlineData("SHA512")]
    [InlineData("SHA1")]
    [InlineData("MD5")]
    [InlineData("something-unrecognized")] // falls back to SHA256 rather than throwing
    public void ComputeSignatureHeaderValue_AllAlgorithms_ProduceNonEmptyDeterministicHex(string algorithm)
    {
        var a = HmacSigner.ComputeSignatureHeaderValue(algorithm, "sekret", "payload", includeTimestamp: false);
        var b = HmacSigner.ComputeSignatureHeaderValue(algorithm, "sekret", "payload", includeTimestamp: false);

        Assert.NotEmpty(a);
        Assert.Equal(a, b); // deterministic for the same inputs
        Assert.Matches("^[0-9a-f]+$", a);
    }

    [Fact]
    public void ComputeSignatureHeaderValue_DifferentPayloads_ProduceDifferentSignatures()
    {
        var a = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "payload-a", includeTimestamp: false);
        var b = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "payload-b", includeTimestamp: false);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeSignatureHeaderValue_DifferentSecrets_ProduceDifferentSignatures()
    {
        var a = HmacSigner.ComputeSignatureHeaderValue("SHA256", "secret-a", "payload", includeTimestamp: false);
        var b = HmacSigner.ComputeSignatureHeaderValue("SHA256", "secret-b", "payload", includeTimestamp: false);
        Assert.NotEqual(a, b);
    }
}
