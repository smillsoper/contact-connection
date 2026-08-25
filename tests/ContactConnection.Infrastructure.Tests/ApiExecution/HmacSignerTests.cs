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

    // ── VerifySignatureHeaderValue — the inbound-webhook mirror of Compute ────────────────────

    [Theory]
    [InlineData("SHA256")]
    [InlineData("SHA512")]
    [InlineData("SHA1")]
    [InlineData("MD5")]
    public void RoundTrip_NoTimestamp_ComputeThenVerify_Succeeds(string algorithm)
    {
        var header = HmacSigner.ComputeSignatureHeaderValue(algorithm, "sekret", "the-payload", includeTimestamp: false);
        Assert.True(HmacSigner.VerifySignatureHeaderValue(algorithm, "sekret", "the-payload", header, includeTimestamp: false));
    }

    [Fact]
    public void RoundTrip_WithTimestamp_ComputeThenVerify_Succeeds()
    {
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "the-payload", includeTimestamp: true);
        Assert.True(HmacSigner.VerifySignatureHeaderValue("SHA256", "sekret", "the-payload", header, includeTimestamp: true));
    }

    [Fact]
    public void Verify_WrongSecret_Fails()
    {
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "the-payload", includeTimestamp: false);
        Assert.False(HmacSigner.VerifySignatureHeaderValue("SHA256", "wrong-secret", "the-payload", header, includeTimestamp: false));
    }

    [Fact]
    public void Verify_TamperedPayload_Fails()
    {
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "the-payload", includeTimestamp: false);
        Assert.False(HmacSigner.VerifySignatureHeaderValue("SHA256", "sekret", "a-different-payload", header, includeTimestamp: false));
    }

    [Fact]
    public void Verify_TamperedHeader_Fails()
    {
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "the-payload", includeTimestamp: false);
        var tampered = header[..^1] + (header[^1] == 'a' ? 'b' : 'a');
        Assert.False(HmacSigner.VerifySignatureHeaderValue("SHA256", "sekret", "the-payload", tampered, includeTimestamp: false));
    }

    [Fact]
    public void Verify_NullOrEmptyHeader_Fails()
    {
        Assert.False(HmacSigner.VerifySignatureHeaderValue("SHA256", "sekret", "payload", null, includeTimestamp: false));
        Assert.False(HmacSigner.VerifySignatureHeaderValue("SHA256", "sekret", "payload", "", includeTimestamp: false));
    }

    [Fact]
    public void Verify_MalformedTimestampedHeader_Fails()
    {
        Assert.False(HmacSigner.VerifySignatureHeaderValue("SHA256", "sekret", "payload", "not-the-right-format", includeTimestamp: true));
        Assert.False(HmacSigner.VerifySignatureHeaderValue("SHA256", "sekret", "payload", "t=abc,v1=deadbeef", includeTimestamp: true));
    }

    [Fact]
    public void Verify_TimestampOutsideTolerance_Fails_EvenWithCorrectSignature()
    {
        var staleTimestamp = DateTimeOffset.UtcNow.AddSeconds(-600).ToUnixTimeSeconds();
        var expectedHash = Convert.ToHexStringLower(new HMACSHA256(Encoding.UTF8.GetBytes("sekret"))
            .ComputeHash(Encoding.UTF8.GetBytes($"{staleTimestamp}.payload")));
        var header = $"t={staleTimestamp},v1={expectedHash}";

        Assert.False(HmacSigner.VerifySignatureHeaderValue("SHA256", "sekret", "payload", header, includeTimestamp: true, toleranceSeconds: 300));
    }

    [Fact]
    public void Verify_TimestampWithinTolerance_Succeeds()
    {
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", "sekret", "payload", includeTimestamp: true);
        Assert.True(HmacSigner.VerifySignatureHeaderValue("SHA256", "sekret", "payload", header, includeTimestamp: true, toleranceSeconds: 300));
    }
}
