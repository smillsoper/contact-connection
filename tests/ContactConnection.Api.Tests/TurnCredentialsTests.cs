using ContactConnection.Api.Endpoints;
using Xunit;

namespace ContactConnection.Api.Tests;

/// <summary>S171: TURN REST API credentials must match what coturn's use-auth-secret computes.</summary>
public class TurnCredentialsTests
{
    [Fact]
    public void Username_IsExpiryColonUser_AndCredential_IsBase64HmacSha1()
    {
        var expires = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var (username, credential) = SoftphoneEndpoints.TurnCredentials("north", "agent-1", expires);

        Assert.Equal("1800000000:agent-1", username);
        // base64(HMAC-SHA1("north", "1800000000:agent-1")) — reference value computed independently.
        using var hmac = new System.Security.Cryptography.HMACSHA1(System.Text.Encoding.UTF8.GetBytes("north"));
        Assert.Equal(Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(username))), credential);
        Assert.Equal(28, credential.Length);   // 20-byte SHA-1 digest in base64
    }
}
