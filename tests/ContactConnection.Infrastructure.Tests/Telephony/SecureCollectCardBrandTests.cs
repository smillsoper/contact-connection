using System.Text.Json.Nodes;
using ContactConnection.Infrastructure.Telephony;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Telephony;

/// <summary>S169: card brand from the PAN's leading digits (standard test card numbers).</summary>
public class SecureCollectCardBrandTests
{
    [Theory]
    [InlineData("4111111111111111", "Visa")]
    [InlineData("4012888888881881", "Visa")]
    [InlineData("5555555555554444", "MasterCard")]
    [InlineData("5105105105105100", "MasterCard")]
    [InlineData("2223003122003222", "MasterCard")]   // 2-series
    [InlineData("378282246310005", "American Express")]
    [InlineData("371449635398431", "American Express")]
    [InlineData("6011111111111117", "Discover")]
    [InlineData("6445644564456445", "Discover")]
    [InlineData("6500000000000002", "Discover")]
    [InlineData("6221260000000000", "Discover")]   // Discover's slice of the 62 range
    [InlineData("6200000000000005", "UnionPay")]
    [InlineData("30569309025904", "Diners Club")]
    [InlineData("36227206271667", "Diners Club")]
    [InlineData("3530111333300000", "JCB")]
    [InlineData("9999999999999995", "Unknown")]
    [InlineData("41", "Unknown")]
    [InlineData("", "Unknown")]
    public void CardBrand_FromLeadingDigits(string pan, string expected)
        => Assert.Equal(expected, SecureCollect.CardBrand(pan));

    [Fact]
    public void FindPan_PicksTheLuhnValidatedField()
    {
        var specs = new JsonArray
        {
            new JsonObject { ["k"] = "card", ["val"] = SecureCollectValidation.Luhn },
            new JsonObject { ["k"] = "exp",  ["val"] = SecureCollectValidation.ExpiryMmyy },
        };
        var captured = new Dictionary<string, string> { ["card"] = "4111111111111111", ["exp"] = "1231" };
        Assert.Equal("4111111111111111", SecureCollect.FindPan(specs, captured));
        Assert.Null(SecureCollect.FindPan(new JsonArray { new JsonObject { ["k"] = "exp", ["val"] = "expiry_mmyy" } }, captured));
    }
}
