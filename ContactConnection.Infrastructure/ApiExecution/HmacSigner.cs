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

    /// <summary>
    /// Verifies an inbound signature header against a recomputed HMAC of the given payload — the
    /// mirror image of ComputeSignatureHeaderValue, for inbound webhook receivers. Only
    /// recognizes the two formats ComputeSignatureHeaderValue itself produces (bare hex, or the
    /// "t=...,v1=..." timestamped form) — no auto-detection of third-party header conventions
    /// (e.g. GitHub's "sha256=" prefix). When includeTimestamp is true, a header whose timestamp
    /// is outside toleranceSeconds of now is rejected (replay protection) even if the signature
    /// itself is otherwise valid. Comparison is constant-time.
    /// </summary>
    public static bool VerifySignatureHeaderValue(string algorithm, string secret, string payload,
        string? headerValue, bool includeTimestamp, int toleranceSeconds = 300)
    {
        payload ??= string.Empty;
        if (string.IsNullOrEmpty(headerValue)) return false;

        if (!includeTimestamp)
            return FixedTimeEquals(Hex(Sign(algorithm, secret, payload)), headerValue);

        var parts = headerValue.Split(',');
        string? tPart = null, v1Part = null;
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t") tPart = kv[1];
            else if (kv[0] == "v1") v1Part = kv[1];
        }
        if (tPart is null || v1Part is null) return false;
        if (!long.TryParse(tPart, out var timestamp)) return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > toleranceSeconds) return false;

        var expected = Hex(Sign(algorithm, secret, $"{timestamp}.{payload}"));
        return FixedTimeEquals(expected, v1Part);
    }

    private static bool FixedTimeEquals(string expectedHex, string actualHex)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
        var actualBytes = Encoding.UTF8.GetBytes(actualHex);
        if (expectedBytes.Length != actualBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
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
