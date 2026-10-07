using ContactConnection.Api.Endpoints;
using Xunit;

namespace ContactConnection.Api.Tests;

public class CallerHistoryTests
{
    [Theory]
    [InlineData("+15416704541", "5416704541")]
    [InlineData("5416704541", "5416704541")]
    [InlineData("(541) 670-4541", "5416704541")]
    [InlineData("6704541", "6704541")]
    [InlineData("anonymous", null)]
    [InlineData("12345", null)]
    [InlineData(null, null)]
    public void Phone_key_is_the_last_ten_digits(string? phone, string? key) => Assert.Equal(key, CallerHistoryEndpoints.PhoneKey(phone));
}
