using System.Text.Json.Nodes;
using ContactConnection.Infrastructure.Telephony;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>Pure helpers for the tf_secure_collect guided-DTMF node — field parsing, the
/// play_and_get_digits length regexp, and per-field post-validation (Luhn / expiry / CVV).</summary>
public class SecureCollectTests
{
    private static JsonArray Fields(params JsonObject[] rows) => new(rows.Cast<JsonNode?>().ToArray());

    [Fact]
    public void ParseFields_ReadsRowsInOrder_AppliesDefaults()
    {
        var arr = Fields(
            new JsonObject { ["key"] = "pan", ["minDigits"] = 13, ["maxDigits"] = 19, ["validation"] = "luhn" },
            new JsonObject { ["key"] = "cvv", ["minDigits"] = 3, ["maxDigits"] = 4 });

        var parsed = SecureCollect.ParseFields(arr);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("pan", parsed[0].Key);
        Assert.Equal(SecureCollectValidation.Luhn, parsed[0].Validation);
        Assert.Equal("#", parsed[0].Terminator);   // max > 1 and no explicit terminator → "#"
        Assert.Equal("cvv", parsed[1].Key);
        Assert.Equal(SecureCollectValidation.None, parsed[1].Validation);
    }

    [Fact]
    public void ParseFields_SkipsRowsWithoutKey_AndClampsMaxBelowMin()
    {
        var arr = Fields(
            new JsonObject { ["minDigits"] = 4 },                               // no key → skipped
            new JsonObject { ["key"] = "x", ["minDigits"] = 6, ["maxDigits"] = 2 }); // max<min → max=min

        var parsed = SecureCollect.ParseFields(arr);

        Assert.Single(parsed);
        Assert.Equal(6, parsed[0].MinDigits);
        Assert.Equal(6, parsed[0].MaxDigits);
    }

    [Fact]
    public void ParseFields_UnknownValidation_FallsBackToNone() =>
        Assert.Equal(SecureCollectValidation.None,
            SecureCollect.ParseFields(Fields(new JsonObject { ["key"] = "a", ["validation"] = "bogus" }))[0].Validation);

    [Fact]
    public void ParseFields_NotAnArray_ReturnsEmpty() =>
        Assert.Empty(SecureCollect.ParseFields(new JsonObject()));

    [Theory]
    [InlineData(4, 4, @"^\d{4}$")]
    [InlineData(13, 19, @"^\d{13,19}$")]
    public void LengthRegexp_MatchesMinMax(int min, int max, string expected) =>
        Assert.Equal(expected, SecureCollect.LengthRegexp(min, max));

    [Theory]
    [InlineData("4242424242424242", true)]   // canonical Visa test PAN
    [InlineData("4111111111111111", true)]
    [InlineData("4242424242424241", false)]  // last digit off by one
    [InlineData("1234567890123456", false)]
    public void Validate_Luhn(string digits, bool expected) =>
        Assert.Equal(expected, SecureCollect.Validate(SecureCollectValidation.Luhn, digits));

    [Theory]
    [InlineData("123", true)]
    [InlineData("1234", true)]
    [InlineData("12", false)]
    [InlineData("12345", false)]
    [InlineData("12a", false)]
    public void Validate_Cvv(string digits, bool expected) =>
        Assert.Equal(expected, SecureCollect.Validate(SecureCollectValidation.Cvv, digits));

    [Fact]
    public void Validate_None_AcceptsAnyDigits_RejectsNonDigits()
    {
        Assert.True(SecureCollect.Validate(SecureCollectValidation.None, "000"));
        Assert.False(SecureCollect.Validate(SecureCollectValidation.None, "0a0"));
        Assert.False(SecureCollect.Validate(SecureCollectValidation.None, ""));
    }

    [Theory]
    [InlineData("1226", true)]   // Dec 2026 — future
    [InlineData("0125", true)]   // Jan 2025 — same month as `now` below, still valid through end of month
    [InlineData("1224", false)]  // Dec 2024 — past
    [InlineData("1325", false)]  // month 13
    [InlineData("0026", false)]  // month 00
    [InlineData("125", false)]   // wrong length
    public void ExpiryValid_AgainstFixedNow(string digits, bool expected)
    {
        var now = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, SecureCollect.ExpiryValid(digits, now));
    }
}
