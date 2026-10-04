using ContactConnection.Domain.ValueObjects;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

public class UsageMeteringTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 17, 0, 0, TimeSpan.Zero);

    private static MeteredCall Call(string source, string? ani, string? dnis, int seconds) =>
        new(source, ani, dnis, T0, T0.AddSeconds(seconds));

    [Theory]
    [InlineData("+15416413898", "5416413898")]
    [InlineData("15416413898", "5416413898")]
    [InlineData("(541) 641-3898", "5416413898")]
    [InlineData("1002", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("+445416413898", null)]
    public void Nanp_normalizes_or_rejects(string? input, string? expected) =>
        Assert.Equal(expected, BillableNumber.Nanp(input));

    [Theory]
    [InlineData("+18001234567", true)]
    [InlineData("8331234567", true)]
    [InlineData("+18881234567", true)]
    [InlineData("+15416413898", false)]
    [InlineData("+18101234567", false)] // 810 is a geographic area code
    [InlineData("1000", false)]
    public void TollFree_by_area_code(string number, bool expected) =>
        Assert.Equal(expected, BillableNumber.IsTollFree(number));

    [Fact]
    public void Inbound_split_by_number_dialed()
    {
        var t = new UsageTally();
        t.Add(Call("inbound", "+15416704541", "+15416413898", 90));
        t.Add(Call("inbound", "+15416704541", "+18001234567", 120));
        t.Add(Call("inbound", "+15035550100", "+18001234567", 60));

        Assert.Equal((1, 90L), (t.InboundLocal.Calls, t.InboundLocal.Seconds));
        Assert.Equal((2, 180L), (t.InboundTollFree.Calls, t.InboundTollFree.Seconds));
        Assert.Equal(3m, t.InboundTollFree.Minutes);
        Assert.Equal(2, t.ByNumber["8001234567"].Calls);
    }

    [Fact]
    public void Internal_calls_are_not_billed()
    {
        var t = new UsageTally();
        t.Add(Call("inbound", "1000", "+18001234567", 300));   // extension test call into a campaign number
        t.Add(Call("inbound", "+15416704541", null, 300));      // no DID
        t.Add(Call("outbound", "1002", null, 300));             // agent-to-agent
        t.Add(Call("manual", null, null, 300));                 // designer test session

        Assert.Equal(4, t.Internal);
        Assert.Equal(0, t.InboundLocal.Calls + t.InboundTollFree.Calls + t.Outbound.Calls);
    }

    [Fact]
    public void Outbound_and_callbacks_count_when_a_real_number_was_dialed()
    {
        var t = new UsageTally();
        t.Add(Call("outbound", "+15416704541", null, 60));
        t.Add(Call("callback", "+15416704541", "+15419196582", 30));

        Assert.Equal((2, 90L), (t.Outbound.Calls, t.Outbound.Seconds));
        Assert.Empty(t.ByNumber);
    }

    [Fact]
    public void Calls_without_an_end_are_held_back()
    {
        var t = new UsageTally();
        t.Add(new MeteredCall("inbound", "+15416704541", "+15416413898", T0, null));

        Assert.Equal(1, t.Unended);
        Assert.Equal(0, t.InboundLocal.Calls);
    }

    [Fact]
    public void Charges_apply_surcharge_to_toll_free_and_the_minimum()
    {
        var t = new UsageTally();
        t.Add(Call("inbound", "+15416704541", "+15416413898", 600));  // 10 min local
        t.Add(Call("outbound", "+15416704541", null, 120));            // 2 min outbound
        t.Add(Call("inbound", "+15416704541", "+18001234567", 300));   // 5 min toll-free

        var c = UsageCharges.Calculate(t, 0.035m, 0.01m, 0m);
        Assert.Equal(0.42m, c.LocalAndOutbound);  // 12 × 0.035
        Assert.Equal(0.23m, c.TollFree);          // 5 × 0.045 = 0.225 → 0.23 (banker's rounding would give 0.22)
        Assert.Equal(0.65m, c.Total);

        var withMinimum = UsageCharges.Calculate(t, 0.035m, 0.01m, 6000m);
        Assert.Equal(6000m, withMinimum.Total);
        Assert.Equal(0.65m, withMinimum.Usage);
    }
}
