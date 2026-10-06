using ContactConnection.Infrastructure.Kpis;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Kpis;

public class KpiCalculatorTests
{
    private static readonly Guid Campaign = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid Agent = Guid.NewGuid();
    private static readonly Guid SaleCat = Guid.NewGuid();
    private static readonly Guid NoSaleCat = Guid.NewGuid();
    private static readonly Guid CsCat = Guid.NewGuid();
    private static readonly Guid LeadCat = Guid.NewGuid();

    private static KpiInteraction Ix(bool opp, string? key, Guid? cat, bool order = false, bool net = false, bool declined = false,
        decimal gross = 0, decimal exclTax = 0, decimal merch = 0, int units = 0, bool upsell = false, bool unmapped = false) =>
        new(Campaign, Client, Agent, opp, key, cat, unmapped, order, net, declined, gross, exclTax, merch, units, upsell, 300);

    /// <summary>10 interactions: 6 sales opportunities (3 sales, one with a voided payment), 3 CS (one with an order the
    /// CS agent placed), 1 unmapped.</summary>
    private static List<KpiInteraction> Sample() =>
    [
        Ix(true, "sale", SaleCat, order: true, net: true, gross: 107.60m, exclTax: 100m, merch: 90m, units: 2, upsell: true),
        Ix(true, "sale", SaleCat, order: true, net: true, gross: 53.80m, exclTax: 50m, merch: 45m, units: 1),
        Ix(true, "sale", SaleCat, order: true, net: false, gross: 53.80m, exclTax: 50m, merch: 45m, units: 1),
        Ix(true, "opportunity_no_sale", NoSaleCat, declined: true),
        Ix(true, "opportunity_no_sale", NoSaleCat),
        Ix(true, "sale", SaleCat),                      // dispositioned a sale, but no order went through
        Ix(false, "customer_service", CsCat, order: true, net: true, gross: 21.52m, exclTax: 20m, merch: 18m, units: 1),
        Ix(false, "customer_service", CsCat),
        Ix(false, null, LeadCat),
        Ix(false, null, null, unmapped: true),
    ];

    [Fact]
    public void CloseRates_RawGrossNet()
    {
        var m = KpiCalculator.Compute(Sample(), [], []);
        Assert.Equal(10, m.Interactions);
        Assert.Equal(6, m.Opportunities);
        Assert.Equal(4, m.Orders);
        Assert.Equal(40.0, m.RawCloseRate);       // 4 orders ÷ 10 interactions
        Assert.Equal(50.0, m.GrossCloseRate);     // 3 orders on opportunities ÷ 6
        Assert.Equal(33.3, m.NetCloseRate);       // 2 net orders on opportunities ÷ 6
        Assert.Equal(1, m.Declines);
        Assert.Equal(1, m.SaleWithoutOrder);
        Assert.Equal(1, m.Unmapped);
    }

    [Fact]
    public void Revenue_EveryBasis_AndNet()
    {
        var m = KpiCalculator.Compute(Sample(), [], [new KpiAgentTime(2 * 3600, 0, 0)]);
        Assert.Equal(236.72m, m.Gross.Total);
        Assert.Equal(220m, m.ExclTax.Total);
        Assert.Equal(198m, m.Merch.Total);
        Assert.Equal(170m, m.ExclTax.Net);        // the voided order's $50 left out
        Assert.Equal(22m, m.ExclTax.PerCall);     // 220 ÷ 10
        Assert.Equal(36.67m, m.ExclTax.PerOpportunity);
        Assert.Equal(55m, m.ExclTax.AverageOrder);
        Assert.Equal(110m, m.ExclTax.PerAgentHour);
        Assert.Equal(25.0, m.UpsellTakeRate);     // 1 of 4 orders
        Assert.Equal(1.25, m.UnitsPerOrder);
    }

    [Fact]
    public void CallHandling_AndAht()
    {
        var calls = new List<KpiCall>
        {
            new(Campaign, Client, true, false, true, 240),
            new(Campaign, Client, true, false, false, 360),
            new(Campaign, Client, false, true, null, 0),
            new(Campaign, Client, false, true, null, 0),
        };
        var m = KpiCalculator.Compute([], calls, [new KpiAgentTime(3600, 120, 2)]);
        Assert.Equal(4, m.CallsOffered);
        Assert.Equal(2, m.CallsHandled);
        Assert.Equal(50.0, m.AbandonRate);
        Assert.Equal(50.0, m.ServiceLevel);
        Assert.Equal(300, m.AvgTalkSeconds);
        Assert.Equal(60, m.AvgAcwSeconds);
        Assert.Equal(360, m.AhtSeconds);
        Assert.Null(m.RawCloseRate);              // nothing to divide by
    }

    [Fact]
    public void CustomKpi_NumeratorOutOfDenominator()
    {
        var kpis = new[]
        {
            new KpiCustomDefinition(Guid.NewGuid(), "Lead capture rate", [LeadCat], [LeadCat, NoSaleCat]),
            new KpiCustomDefinition(Guid.NewGuid(), "CS share", [CsCat], []),
        };
        var m = KpiCalculator.Compute(Sample(), [], [], kpis);
        Assert.Equal(33.3, m.Custom[0].Percent);  // 1 lead ÷ (1 lead + 2 no-sale)
        Assert.Equal(20.0, m.Custom[1].Percent);  // 2 CS ÷ all 10
    }

    [Fact]
    public void AgentTime_ClipsToTheWindow_AndCountsAcw()
    {
        var since = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var until = since.AddHours(2);
        var history = new (Guid, string, DateTimeOffset)[]
        {
            (Agent, "available", since.AddHours(-1)),   // already logged in when the window opened
            (Agent, "on_call", since.AddMinutes(30)),
            (Agent, "acw", since.AddMinutes(40)),
            (Agent, "available", since.AddMinutes(45)),
            (Agent, "logged_out", since.AddMinutes(90)),
        };
        var t = KpiCalculator.AgentTime(history, since, until)[Agent];
        Assert.Equal(90 * 60, t.LoggedInSeconds);
        Assert.Equal(5 * 60, t.AcwSeconds);
        Assert.Equal(1, t.AcwSegments);
    }
}
