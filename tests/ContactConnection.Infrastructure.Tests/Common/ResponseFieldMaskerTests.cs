using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Common;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Common;

/// <summary>Covers ResponseFieldMasker — the shared masking helper behind the admin/portal "Test"
/// button's response preview and the api_call node handlers' write into
/// flow_sessions.variable_store. See API_HARDENING_CHECKLIST.md Tier 3.</summary>
public class ResponseFieldMaskerTests
{
    // ── ParsePaths ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void ParsePaths_NullBlankOrMalformedOrEmpty_ReturnsEmptyList(string? json)
    {
        Assert.Empty(ResponseFieldMasker.ParsePaths(json));
    }

    [Fact]
    public void ParsePaths_ValidArray_ReturnsTrimmedNonEmptyPaths()
    {
        var paths = ResponseFieldMasker.ParsePaths("[\"ssn\", \" customer.dob \", \"\", \"  \"]");
        Assert.Equal(["ssn", "customer.dob"], paths);
    }

    // ── MaskJson ─────────────────────────────────────────────────────────────

    [Fact]
    public void MaskJson_NoPaths_ReturnsBodyUnchanged()
    {
        const string body = "{\"ssn\":\"123-45-6789\"}";
        Assert.Same(body, ResponseFieldMasker.MaskJson(body, []));
    }

    [Fact]
    public void MaskJson_NotJson_ReturnsBodyUnchanged()
    {
        const string body = "plain text response, not JSON";
        Assert.Equal(body, ResponseFieldMasker.MaskJson(body, ["ssn"]));
    }

    [Fact]
    public void MaskJson_TopLevelField_Redacted()
    {
        var masked = ResponseFieldMasker.MaskJson("{\"ssn\":\"123-45-6789\",\"name\":\"Jane\"}", ["ssn"]);
        Assert.Contains("\"ssn\":\"[REDACTED]\"", masked);
        Assert.Contains("\"name\":\"Jane\"", masked); // untouched
    }

    [Fact]
    public void MaskJson_NestedField_Redacted()
    {
        var masked = ResponseFieldMasker.MaskJson(
            "{\"customer\":{\"dob\":\"1990-01-01\",\"name\":\"Jane\"}}", ["customer.dob"]);
        Assert.Contains("\"dob\":\"[REDACTED]\"", masked);
        Assert.Contains("\"name\":\"Jane\"", masked);
    }

    [Fact]
    public void MaskJson_MultiplePaths_AllRedacted()
    {
        var masked = ResponseFieldMasker.MaskJson(
            "{\"ssn\":\"123\",\"payment\":{\"cardNumber\":\"4111\"}}", ["ssn", "payment.cardNumber"]);
        Assert.Contains("\"ssn\":\"[REDACTED]\"", masked);
        Assert.Contains("\"cardNumber\":\"[REDACTED]\"", masked);
    }

    [Fact]
    public void MaskJson_PathNotPresentInResponse_NoOp_ReturnsOriginalString()
    {
        const string body = "{\"name\":\"Jane\"}";
        var masked = ResponseFieldMasker.MaskJson(body, ["ssn", "customer.dob"]);
        Assert.Equal(body, masked); // unchanged content — nothing matched
    }

    [Fact]
    public void MaskJson_PartialPathMissingIntermediateObject_NoOp()
    {
        // "customer" isn't an object here (it's a string) — the nested path can't resolve.
        const string body = "{\"customer\":\"just a string\"}";
        var masked = ResponseFieldMasker.MaskJson(body, ["customer.dob"]);
        Assert.Equal(body, masked);
    }

    [Fact]
    public void MaskJson_ArrayResponse_NoOp_TopLevelPathsOnlyTargetObjects()
    {
        const string body = "[{\"ssn\":\"123\"}]";
        // Deliberately minimal (dot-paths only, no array indexing) — a path can't reach into a
        // top-level array. Documented limitation, not a bug.
        var masked = ResponseFieldMasker.MaskJson(body, ["ssn"]);
        Assert.Equal(body, masked);
    }

    // ── Mask (ApiDefinitionExecutionResult overload) ────────────────────────

    private static ApiDefinitionExecutionResult NewResult(string? body) =>
        new(Success: true, StatusCode: 200, StatusMessage: "OK", ResponseHeaders: [], ResponseBody: body, TimedOut: false, Error: null);

    [Fact]
    public void Mask_NullResponseBody_ReturnsSameInstance()
    {
        var result = NewResult(null);
        Assert.Same(result, ResponseFieldMasker.Mask(result, "[\"ssn\"]"));
    }

    [Fact]
    public void Mask_NoFieldsConfigured_ReturnsSameInstance()
    {
        var result = NewResult("{\"ssn\":\"123\"}");
        Assert.Same(result, ResponseFieldMasker.Mask(result, "[]"));
        Assert.Same(result, ResponseFieldMasker.Mask(result, null));
    }

    [Fact]
    public void Mask_FieldsConfigured_ReturnsNewResultWithMaskedBody_OtherFieldsUntouched()
    {
        var result = NewResult("{\"ssn\":\"123-45-6789\"}");

        var masked = ResponseFieldMasker.Mask(result, "[\"ssn\"]");

        Assert.NotSame(result, masked);
        Assert.Contains("[REDACTED]", masked.ResponseBody);
        Assert.True(masked.Success);
        Assert.Equal(200, masked.StatusCode);
        Assert.Equal("OK", masked.StatusMessage);
    }

    [Fact]
    public void Mask_NothingActuallyMatchedInThisResponse_ReturnsSameInstance()
    {
        var result = NewResult("{\"name\":\"Jane\"}");
        // "ssn" is configured but doesn't appear in this particular response — no change needed.
        Assert.Same(result, ResponseFieldMasker.Mask(result, "[\"ssn\"]"));
    }
}
