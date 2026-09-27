using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

public class OrderNumberSequenceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    [Fact]
    public void Format_PadsToWidth_WithPrefixAndSuffix()
    {
        var seq = OrderNumberSequence.Create(Tenant, Client, "LIFSEA-", "", 8, 10000000);
        Assert.Equal("LIFSEA-10000000", seq.Format(seq.NextValue));
        Assert.Equal("LIFSEA-00000042", seq.Format(42));

        var withSuffix = OrderNumberSequence.Create(Tenant, Client, "A", "-X", 4, 7);
        Assert.Equal("A0007-X", withSuffix.Format(7));
    }

    [Fact]
    public void Format_ValueWiderThanWidth_IsNotTruncated()
        => Assert.Equal("P12345", OrderNumberSequence.Format("P", "", 3, 12345));

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    public void Create_RejectsWidthOutOfRange(int width)
        => Assert.Throws<ArgumentException>(() => OrderNumberSequence.Create(Tenant, Client, "", "", width, 1));

    [Fact]
    public void Create_RejectsNegativeNextValue()
        => Assert.Throws<ArgumentException>(() => OrderNumberSequence.Create(Tenant, Client, "X", "", 5, -1));

    [Fact]
    public void Create_RejectsWhitespaceInPrefixOrSuffix()
    {
        Assert.Throws<ArgumentException>(() => OrderNumberSequence.Create(Tenant, Client, "LS ", "", 5, 1));
        Assert.Throws<ArgumentException>(() => OrderNumberSequence.Create(Tenant, Client, "LS", " A", 5, 1));
    }

    [Fact]
    public void Create_RejectsFormatsLongerThanGatewayLimit()
    {
        // 13 + 8 = 21 > 20 (Authorize.Net's invoiceNumber/refId cap)
        Assert.Throws<ArgumentException>(() => OrderNumberSequence.Create(Tenant, Client, "LIFESEASONS-X", "", 8, 1));
        // Exactly 20 is fine
        OrderNumberSequence.Create(Tenant, Client, "LIFESEASONS-", "", 8, 1);
    }

    [Fact]
    public void Create_LengthCheckAccountsForNextValueWiderThanWidth()
        // width 4 but next value has 12 digits: "PREFIX-" (7) + 12 = 19 fits; + "XX" = 21 doesn't.
        => Assert.Throws<ArgumentException>(() =>
            OrderNumberSequence.Create(Tenant, Client, "PREFIX-", "XX", 4, 100000000000));

    [Fact]
    public void Update_ChangesFormatAndNextValue_AndValidates()
    {
        var seq = OrderNumberSequence.Create(Tenant, Client, "LIFSEA-", "", 8, 10000000);
        seq.Update("NQ-", "", 6, 500);
        Assert.Equal("NQ-000500", seq.Format(seq.NextValue));

        Assert.Throws<ArgumentException>(() => seq.Update("NQ-", "", 0, 500));
        Assert.Equal("NQ-000500", seq.Format(seq.NextValue)); // unchanged after rejected update
    }
}
