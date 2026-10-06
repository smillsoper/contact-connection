using ContactConnection.Infrastructure.Kpis;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Kpis;

/// <summary>Formula KPIs (S181) — NCalc over the KPI variables.</summary>
public class KpiFormulaTests
{
    private static readonly Guid Campaign = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid CsCat = Guid.NewGuid();
    private static readonly Guid SaveCat = Guid.NewGuid();
    private static readonly Guid Saved = Guid.NewGuid();
    private static readonly Guid Cancelled = Guid.NewGuid();

    [Fact]
    public void Names_ArePascalCasedWithAPrefix_AndCollisionsGetASuffix()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var names = KpiFormula.Names([(a, "Transferred to Customer Service"), (b, "Transferred to customer-service!")], "Disp_");
        Assert.Equal("Disp_TransferredToCustomerService", names[a]);
        Assert.Equal("Disp_TransferredToCustomerService2", names[b]);
    }

    [Fact]
    public void Validate_CatchesSyntax_AndUnknownVariables()
    {
        var known = new[] { "Orders", "CallsHandled" };
        Assert.Null(KpiFormula.Validate("Orders / CallsHandled", known));
        Assert.Contains("Unknown variable", KpiFormula.Validate("Orders / Callz", known));
        Assert.Contains("isn't valid", KpiFormula.Validate("Orders / (CallsHandled", known));
        Assert.NotNull(KpiFormula.Validate("  ", known));
    }

    [Fact]
    public void Evaluate_DivideByZeroIsNull_NotACrash()
    {
        Assert.Equal(0.5, KpiFormula.Evaluate("Orders / CallsHandled", new Dictionary<string, double> { ["Orders"] = 2, ["CallsHandled"] = 4 }));
        Assert.Null(KpiFormula.Evaluate("Orders / CallsHandled", new Dictionary<string, double> { ["Orders"] = 2, ["CallsHandled"] = 0 }));
        Assert.Null(KpiFormula.Evaluate("Orders / (", new Dictionary<string, double>()));
    }

    private static KpiInteraction Ix(Guid category, Guid disposition, bool order = false, decimal exclTax = 0) =>
        new(Campaign, Client, Guid.NewGuid(), false, null, category, false, order, order, false,
            exclTax, exclTax, exclTax, order ? 1 : 0, false, 120, disposition);

    [Fact]
    public void FormulaKpis_SaveRate_RevenuePerHandledCall_AndFormats()
    {
        var names = new KpiVariableNames(
            new Dictionary<Guid, string> { [CsCat] = "Cat_CustomerService", [SaveCat] = "Cat_RetentionSave" },
            new Dictionary<Guid, string> { [Saved] = "Disp_Saved", [Cancelled] = "Disp_Cancelled" });
        var ix = new List<KpiInteraction>
        {
            Ix(SaveCat, Saved, order: true, exclTax: 60m), Ix(SaveCat, Saved),
            Ix(CsCat, Cancelled), Ix(CsCat, Cancelled), Ix(CsCat, Cancelled), Ix(CsCat, Cancelled),
        };
        var calls = Enumerable.Range(0, 4).Select(_ => new KpiCall(Campaign, Client, true, false, true, 100)).ToList();
        var kpis = new[]
        {
            new KpiCustomDefinition(Guid.NewGuid(), "Save rate", [], [], KpiCustomKind.Formula, "Disp_Saved / (Disp_Saved + Disp_Cancelled)", KpiFormat.Percent),
            new KpiCustomDefinition(Guid.NewGuid(), "Revenue per handled call", [], [], KpiCustomKind.Formula, "RevenueExclTax / CallsHandled", KpiFormat.Currency),
            new KpiCustomDefinition(Guid.NewGuid(), "Avg handle", [], [], KpiCustomKind.Formula, "HandleSeconds / Interactions", KpiFormat.Duration),
            new KpiCustomDefinition(Guid.NewGuid(), "Nothing", [], [], KpiCustomKind.Formula, "Orders / CallsAbandoned", KpiFormat.Number),
            new KpiCustomDefinition(Guid.NewGuid(), "CS share", [CsCat], []),
        };

        var m = KpiCalculator.Compute(ix, calls, [], kpis, names);

        Assert.Equal(33.3, m.Custom[0].Value);      // 2 saved ÷ 6
        Assert.Equal(15.0, m.Custom[1].Value);      // $60 ÷ 4 handled
        Assert.Equal(120.0, m.Custom[2].Value);     // seconds
        Assert.Null(m.Custom[3].Value);             // no abandons → "—"
        Assert.Equal(66.7, m.Custom[4].Value);      // ratio KPIs still work
        Assert.Equal(KpiFormat.Currency, m.Custom[1].Format);
    }
}
