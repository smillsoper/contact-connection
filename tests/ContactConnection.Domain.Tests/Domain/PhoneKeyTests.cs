using ContactConnection.Domain.ValueObjects;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

public class PhoneKeyTests
{
    [Fact]
    public void Dnis_filter_matches_any_format_and_no_filter_matches_everything()
    {
        var filter = PhoneKey.Set(["+15416413898", "(503) 555-0100"]);
        Assert.True(PhoneKey.Matches(filter, "5416413898"));
        Assert.True(PhoneKey.Matches(filter, "+1 503 555 0100"));
        Assert.False(PhoneKey.Matches(filter, "+15416704541"));
        Assert.False(PhoneKey.Matches(filter, null));
        Assert.True(PhoneKey.Matches(null, "anything"));
        Assert.Null(PhoneKey.Set([]));
        Assert.Null(PhoneKey.Set(["anonymous"]));
    }
}
