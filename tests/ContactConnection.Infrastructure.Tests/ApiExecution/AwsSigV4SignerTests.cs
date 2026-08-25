using ContactConnection.Infrastructure.ApiExecution;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.ApiExecution;

/// <summary>Covers AwsSigV4Signer — the pure signing helper behind the "aws_sigv4" auth type.
/// Verified against the official AWS SigV4 test suite ("aws-sig-v4-test-suite", published by AWS
/// and mirrored at github.com/mongodb/libmongocrypt and npm's @saibotsivad/aws-sig-v4-test-suite)
/// rather than hand-derived expected values, so a subtle algorithm bug can't hide behind a
/// self-consistent but wrong test. The three vectors used here (get-vanilla, post-vanilla,
/// get-vanilla-query-order-key) exercise the GET/POST method variance, the empty-payload hash
/// path, and canonical query string sorting with duplicate keys and mixed-case values — the parts
/// of the spec most likely to have an off-by-one or ordering bug.
/// See API_HARDENING_CHECKLIST.md Tier 3.</summary>
public class AwsSigV4SignerTests
{
    private const string AccessKeyId = "AKIDEXAMPLE";
    private const string SecretAccessKey = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY";
    private const string Region = "us-east-1";
    private const string Service = "service";
    private static readonly DateTimeOffset RequestTime =
        new(2015, 8, 30, 12, 36, 0, TimeSpan.Zero);

    [Fact]
    public void Sign_GetVanilla_MatchesOfficialTestVector()
    {
        var result = AwsSigV4Signer.Sign(
            method: "GET", path: "/", rawQuery: "", host: "example.amazonaws.com",
            payload: null, accessKeyId: AccessKeyId, secretAccessKey: SecretAccessKey,
            sessionToken: null, region: Region, service: Service, requestTime: RequestTime);

        Assert.Equal("20150830T123600Z", result.AmzDate);
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20150830/us-east-1/service/aws4_request, " +
            "SignedHeaders=host;x-amz-date, " +
            "Signature=5fa00fa31553b73ebf1942676e86291e8372ff2a2260956d9b8aae1d763fbf31",
            result.AuthorizationHeader);
    }

    [Fact]
    public void Sign_PostVanilla_MatchesOfficialTestVector()
    {
        var result = AwsSigV4Signer.Sign(
            method: "POST", path: "/", rawQuery: "", host: "example.amazonaws.com",
            payload: null, accessKeyId: AccessKeyId, secretAccessKey: SecretAccessKey,
            sessionToken: null, region: Region, service: Service, requestTime: RequestTime);

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20150830/us-east-1/service/aws4_request, " +
            "SignedHeaders=host;x-amz-date, " +
            "Signature=5da7c1a2acd57cee7505fc6676e4e544621c30862966e37dddb68e92efbe5d6b",
            result.AuthorizationHeader);
    }

    [Fact]
    public void Sign_GetVanillaQueryOrderKey_DuplicateKeysSortedByValue_MatchesOfficialTestVector()
    {
        // "Param1=value2&Param1=Value1" — same key twice with different-case values. The
        // canonical query string must sort by encoded value when keys are equal, and ordinal
        // comparison puts uppercase 'V' before lowercase 'v', so "Value1" sorts before "value2".
        var result = AwsSigV4Signer.Sign(
            method: "GET", path: "/", rawQuery: "Param1=value2&Param1=Value1", host: "example.amazonaws.com",
            payload: null, accessKeyId: AccessKeyId, secretAccessKey: SecretAccessKey,
            sessionToken: null, region: Region, service: Service, requestTime: RequestTime);

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20150830/us-east-1/service/aws4_request, " +
            "SignedHeaders=host;x-amz-date, " +
            "Signature=eedbc4e291e521cf13422ffca22be7d2eb8146eecf653089df300a15b2382bd1",
            result.AuthorizationHeader);
    }

    [Fact]
    public void BuildCanonicalQueryString_DuplicateKeys_SortsByEncodedValueOrdinal()
    {
        var canonical = AwsSigV4Signer.BuildCanonicalQueryString("Param1=value2&Param1=Value1");

        Assert.Equal("Param1=Value1&Param1=value2", canonical);
    }

    [Fact]
    public void BuildCanonicalUri_EmptyPath_ReturnsSlash()
    {
        Assert.Equal("/", AwsSigV4Signer.BuildCanonicalUri(""));
    }

    [Fact]
    public void BuildCanonicalUri_EncodesSegmentsButNotSlash()
    {
        var canonical = AwsSigV4Signer.BuildCanonicalUri("/a path/b+c");

        Assert.Equal("/a%20path/b%2Bc", canonical);
    }

    [Fact]
    public void Sign_SessionToken_AddsSecurityTokenHeaderToSignedHeaders()
    {
        var withoutToken = AwsSigV4Signer.Sign(
            "GET", "/", "", "example.amazonaws.com", null,
            AccessKeyId, SecretAccessKey, sessionToken: null, Region, Service, RequestTime);
        var withToken = AwsSigV4Signer.Sign(
            "GET", "/", "", "example.amazonaws.com", null,
            AccessKeyId, SecretAccessKey, sessionToken: "a-session-token", Region, Service, RequestTime);

        Assert.Contains("SignedHeaders=host;x-amz-date,", withoutToken.AuthorizationHeader);
        Assert.Contains("SignedHeaders=host;x-amz-date;x-amz-security-token,", withToken.AuthorizationHeader);
        Assert.Equal("a-session-token", withToken.SecurityToken);
        // The security token being part of the signed headers means it changes the signature too.
        Assert.NotEqual(withoutToken.AuthorizationHeader, withToken.AuthorizationHeader);
    }

    [Fact]
    public void Sign_DifferentPayload_ProducesDifferentSignature()
    {
        var a = AwsSigV4Signer.Sign(
            "POST", "/", "", "example.amazonaws.com", "payload-a",
            AccessKeyId, SecretAccessKey, null, Region, Service, RequestTime);
        var b = AwsSigV4Signer.Sign(
            "POST", "/", "", "example.amazonaws.com", "payload-b",
            AccessKeyId, SecretAccessKey, null, Region, Service, RequestTime);

        Assert.NotEqual(a.AuthorizationHeader, b.AuthorizationHeader);
    }

    [Fact]
    public void Sign_DifferentSecret_ProducesDifferentSignature()
    {
        var a = AwsSigV4Signer.Sign(
            "GET", "/", "", "example.amazonaws.com", null,
            AccessKeyId, "secret-one", null, Region, Service, RequestTime);
        var b = AwsSigV4Signer.Sign(
            "GET", "/", "", "example.amazonaws.com", null,
            AccessKeyId, "secret-two", null, Region, Service, RequestTime);

        Assert.NotEqual(a.AuthorizationHeader, b.AuthorizationHeader);
    }

    [Fact]
    public void Sign_IsDeterministic_SameInputsSameOutput()
    {
        var a = AwsSigV4Signer.Sign(
            "GET", "/orders", "status=open", "api.example.com", "body",
            AccessKeyId, SecretAccessKey, "tok", "us-west-2", "execute-api", RequestTime);
        var b = AwsSigV4Signer.Sign(
            "GET", "/orders", "status=open", "api.example.com", "body",
            AccessKeyId, SecretAccessKey, "tok", "us-west-2", "execute-api", RequestTime);

        Assert.Equal(a.AuthorizationHeader, b.AuthorizationHeader);
    }
}
