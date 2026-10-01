using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Media;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Media;

/// <summary>S171: the call_record.media object scripts and Liquid read.</summary>
public class MediaAttributionJsonTests
{
    [Fact]
    public void Fields_KeepTheirNames_AndGetTagSafeAliases()
    {
        var m = new MediaAttribution(Guid.NewGuid(), "national", "Cannella", "CNN", "TV", "LF",
            new DateOnly(2026, 10, 1), "+18005551234",
            new Dictionary<string, string> { ["ACCESS CODE"] = "A123", ["PRODUCTCODE"] = "NQ90" });

        var json = MediaAttributionJson.ToJson(m);

        Assert.Equal("Cannella", (string?)json["agency"]);
        Assert.Equal("2026-10-01", (string?)json["start_date"]);
        Assert.Equal("A123", (string?)json["fields"]!["ACCESS CODE"]);
        Assert.Equal("A123", (string?)json["fields"]!["access_code"]);
        Assert.Equal("NQ90", (string?)json["fields"]!["productcode"]);
    }

    [Theory]
    [InlineData("ACCESS CODE", "access_code")]
    [InlineData("Product-Code", "product_code")]
    [InlineData("  Show Length  ", "show_length")]
    public void Alias_IsLowercaseWithUnderscores(string name, string alias) => Assert.Equal(alias, MediaAttributionJson.Alias(name));
}
