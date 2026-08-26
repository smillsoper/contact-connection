using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace ContactConnection.Infrastructure.ApiExecution;

/// <summary>Result of signing a request with AWS Signature Version 4 — the caller applies these
/// as headers (X-Amz-Date, X-Amz-Security-Token if present, Authorization).</summary>
public record AwsSigV4Result(string AmzDate, string? SecurityToken, string AuthorizationHeader);

/// <summary>
/// AWS Signature Version 4 request signing — pure computation, no I/O, no mutation of the
/// request. See API_HARDENING_CHECKLIST.md Tier 3. Mirrors HmacSigner's shape: a static class the
/// caller (ApiDefinitionExecutor / ApiEndpointTestHelper) invokes and then applies the result's
/// headers itself.
///
/// Signs the minimal required header set only — host, x-amz-date, and x-amz-security-token when a
/// session token is configured — matching AWS's own documented minimum ("For the purpose of
/// calculating an authorization signature, only the host and any x-amz-* headers are required").
/// Does not attempt to sign arbitrary caller-supplied headers or a customizable payload subset
/// (unlike the hmac auth type's payloadTemplate) — SigV4 is a spec-mandated algorithm signing the
/// literal outgoing payload, not a vendor-specific convention with room for a template override.
///
/// Verified against the official AWS SigV4 test suite ("aws-sig-v4-test-suite", access key
/// AKIDEXAMPLE / secret wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY, region us-east-1, service
/// "service") — see AwsSigV4SignerTests.
/// </summary>
public static class AwsSigV4Signer
{
    private const string Algorithm = "AWS4-HMAC-SHA256";

    public static AwsSigV4Result Sign(
        string method,
        string path,
        string rawQuery,
        string host,
        string? payload,
        string accessKeyId,
        string secretAccessKey,
        string? sessionToken,
        string region,
        string service,
        DateTimeOffset requestTime)
    {
        var utc = requestTime.ToUniversalTime();
        var dateStamp = utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var amzDate = utc.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);

        // SortedDictionary with ordinal comparison keeps header lines emitted in the required
        // sorted order for free — host < x-amz-date < x-amz-security-token alphabetically.
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-date"] = amzDate,
        };
        if (!string.IsNullOrEmpty(sessionToken))
            headers["x-amz-security-token"] = sessionToken;

        var canonicalHeaderBlock = string.Concat(
            headers.Select(kv => $"{kv.Key}:{CollapseWhitespace(kv.Value.Trim())}\n"));
        var signedHeaders = string.Join(";", headers.Keys);

        var canonicalUri = BuildCanonicalUri(path);
        var canonicalQueryString = BuildCanonicalQueryString(rawQuery);
        var hashedPayload = Hex(Sha256(payload ?? ""));

        // canonicalHeaderBlock already ends with "\n" per header line, so joining it into this
        // list with "\n" separators naturally produces the spec-required blank line between the
        // last header and SignedHeaders (verified against the official test vectors).
        var canonicalRequest = string.Join("\n",
            method.ToUpperInvariant(), canonicalUri, canonicalQueryString, canonicalHeaderBlock, signedHeaders, hashedPayload);

        var credentialScope = $"{dateStamp}/{region}/{service}/aws4_request";
        var stringToSign = string.Join("\n", Algorithm, amzDate, credentialScope, Hex(Sha256(canonicalRequest)));

        var signingKey = DeriveSigningKey(secretAccessKey, dateStamp, region, service);
        var signature = Hex(HmacSha256(signingKey, stringToSign));

        var authorizationHeader =
            $"{Algorithm} Credential={accessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        return new AwsSigV4Result(amzDate, sessionToken, authorizationHeader);
    }

    /// <summary>Percent-encodes each path segment per AWS's UriEncode rule, preserving '/' as the
    /// segment separator (never encoded). Decodes first so a segment that arrived already
    /// percent-encoded isn't double-encoded.</summary>
    internal static string BuildCanonicalUri(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        var segments = path.Split('/').Select(seg => Uri.EscapeDataString(Uri.UnescapeDataString(seg)));
        return string.Join("/", segments);
    }

    /// <summary>Re-encodes and sorts query parameters per AWS's canonical query string rule:
    /// URI-encode each name/value independently, then sort by encoded name, then encoded value.
    /// Uses HttpUtility.ParseQueryString (not manual '&amp;'/'=' splitting) so it correctly
    /// decodes '+'-as-space the same way the query string was originally encoded, and correctly
    /// aggregates repeated keys (e.g. "Param1=value2&amp;Param1=Value1") into multiple values.</summary>
    internal static string BuildCanonicalQueryString(string? rawQuery)
    {
        var nvc = HttpUtility.ParseQueryString(rawQuery ?? "");
        if (nvc.Count == 0) return "";

        var pairs = new List<(string Key, string Value)>();
        foreach (var key in nvc.AllKeys)
        {
            if (key is null) continue;
            var encodedKey = Uri.EscapeDataString(key);
            foreach (var value in nvc.GetValues(key) ?? [])
                pairs.Add((encodedKey, Uri.EscapeDataString(value)));
        }

        pairs.Sort((a, b) =>
        {
            var byKey = string.CompareOrdinal(a.Key, b.Key);
            return byKey != 0 ? byKey : string.CompareOrdinal(a.Value, b.Value);
        });

        return string.Join("&", pairs.Select(p => $"{p.Key}={p.Value}"));
    }

    private static byte[] DeriveSigningKey(string secretAccessKey, string dateStamp, string region, string service)
    {
        var kDate    = HmacSha256Bytes(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), dateStamp);
        var kRegion  = HmacSha256Bytes(kDate, region);
        var kService = HmacSha256Bytes(kRegion, service);
        return HmacSha256Bytes(kService, "aws4_request");
    }

    private static string CollapseWhitespace(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "[ \t]+", " ");

    private static byte[] Sha256(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));

    private static byte[] HmacSha256(byte[] key, string data) => HmacSha256Bytes(key, data);

    private static byte[] HmacSha256Bytes(byte[] key, string data) =>
        new HMACSHA256(key).ComputeHash(Encoding.UTF8.GetBytes(data));

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
