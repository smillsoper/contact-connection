using System.Security.Cryptography;
using System.Text;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>
/// Shared HMAC request-signing helper for the "hmac" auth type — used by both
/// ApiDefinitionExecutor (flow engine api_call nodes) and ApiEndpointTestHelper (admin/portal
/// "Test" button + FlowSessionsEndpoints' live address validation/ZIP/autocomplete resolution),
/// so the signing convention only needs to be defined once. See API_HARDENING_CHECKLIST.md Tier 2.
///
/// What gets signed (the "payload") is decided by the caller — either the definition's
/// configured payloadTemplate (resolved through the same {{ns.field}} tag mechanism as the
/// request body/headers/query params, letting the signed string pull in fields that aren't
/// necessarily in the outgoing body at all) or, when no template is configured, the request's
/// actual outgoing body. This class only turns "the payload" into a header value.
/// </summary>
public static class HmacSigner
{
    /// <summary>
    /// Produces the value to place in the configured signature header:
    ///   - includeTimestamp = false: hex(HMAC(secret, payload))
    ///   - includeTimestamp = true:  "t={unixSeconds},v1={hex(HMAC(secret, "{unixSeconds}.{payload}"))}"
    /// The timestamped form mirrors the Stripe/Svix webhook-signature convention — self-contained
    /// in a single header (no second "timestamp" header needs its own config field), and a
    /// receiver verifying it can still reject a stale signature by checking t against its own
    /// clock.
    /// </summary>
    public static string ComputeSignatureHeaderValue(string algorithm, string secret, string? payload, bool includeTimestamp)
    {
        payload ??= string.Empty;
        if (!includeTimestamp)
            return Hex(Sign(algorithm, secret, payload));

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signed = Sign(algorithm, secret, $"{timestamp}.{payload}");
        return $"t={timestamp},v1={Hex(signed)}";
    }

    private static byte[] Sign(string algorithm, string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        using HMAC hmac = algorithm.ToUpperInvariant() switch
        {
            "SHA512" => new HMACSHA512(key),
            "SHA1" => new HMACSHA1(key),
            "MD5" => new HMACMD5(key),
            _ => new HMACSHA256(key), // "SHA256" and any unrecognized value default here
        };
        return hmac.ComputeHash(data);
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
