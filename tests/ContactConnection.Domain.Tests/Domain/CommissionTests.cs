using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>S171 commissions: rule math, tier replacement, pay periods and the reversal ledger.</summary>
public class CommissionTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Campaign = Guid.NewGuid();
    private static readonly Guid NeuroQ = Guid.NewGuid(), DhaBoost = Guid.NewGuid();

    private static CommissionRule Rule(string kind, decimal amount, string? tier = null,
        Guid? product = null, string? field = null, string? value = null, bool active = true)
    {
        var r = CommissionRule.Create(Tenant, null, Campaign);
        r.Set($"{kind} {amount} {tier}", kind, amount, product, product is null ? null : "SKU", field, value, tier, active);
        return r;
    }

    private static CartItem Item(Guid productId, string sku, int qty, decimal price) => new(
        Guid.NewGuid(), productId, sku, sku, qty, price, price * qty, 0, 0, 0, false, false, false, false, 0,
        false, 0, null, null, null, null, [], [], [], 0, 0, 0, 0);

    // NeuroQ-style order: $119.85 merchandise + $9.95 shipping + $8.00 tax + $0.27 fee = $138.07.
    private static CartDocument Cart() => CartDocument.Empty() with
    {
        Items = [Item(NeuroQ, "NQ-3", 3, 39.95m), Item(DhaBoost, "DHA", 1, 0m)],
        CartSubtotal = 119.85m, Shipping = 9.95m, SalesTax = 8.00m,
        Fees = [new CartFee("CO_RDF", "Retail Delivery Fee", 0.27m)], CartTotal = 138.07m,
    };

    private static CommissionCallFacts Facts(string? tier = null, bool ordered = true, Dictionary<string, string>? fields = null) =>
        new(ordered, Cart(), tier, fields ?? []);

    [Fact]
    public void NeuroQ_AlphaGetsTenPercent_EveryoneElseOnePercent_OfTotalLessShippingTaxAndFees()
    {
        var rules = new[] { Rule(CommissionKind.PercentOfOrder, 10, tier: "Alpha"), Rule(CommissionKind.PercentOfOrder, 1) };

        var alpha = Assert.Single(CommissionCalculator.Calculate(Facts("Alpha"), rules));
        Assert.Equal(119.85m, alpha.Basis);
        Assert.Equal(11.99m, alpha.Amount);   // 11.985 rounds half away from zero

        var regular = Assert.Single(CommissionCalculator.Calculate(Facts(), rules));
        Assert.Equal(1.20m, regular.Amount);

        var elite = Assert.Single(CommissionCalculator.Calculate(Facts("Elite"), rules));   // no Elite rule → general
        Assert.Equal(1.20m, elite.Amount);
    }

    [Fact]
    public void OrderRules_NeedASubmittedOrder_FieldRulesDoNot()
    {
        var rules = new[]
        {
            Rule(CommissionKind.FlatPerOrder, 2),
            Rule(CommissionKind.FlatPerField, 5, field: "save_method", value: "Discount"),
        };
        var lines = CommissionCalculator.Calculate(Facts(ordered: false, fields: new() { ["save_method"] = "discount" }), rules);

        var line = Assert.Single(lines);
        Assert.Equal(CommissionKind.FlatPerField, line.Kind);   // case-insensitive value match
        Assert.Equal(5m, line.Amount);
    }

    [Fact]
    public void PerProduct_PaysPerUnit_AndSkipsOrdersWithoutTheProduct()
    {
        var rules = new[] { Rule(CommissionKind.FlatPerProduct, 1.50m, product: NeuroQ), Rule(CommissionKind.FlatPerProduct, 3m, product: Guid.NewGuid()) };
        var line = Assert.Single(CommissionCalculator.Calculate(Facts(), rules));
        Assert.Equal(3, line.Basis);
        Assert.Equal(4.50m, line.Amount);
    }

    [Fact]
    public void DifferentKinds_Stack_AndInactiveOrZeroRulesPayNothing()
    {
        var rules = new[]
        {
            Rule(CommissionKind.PercentOfOrder, 1),
            Rule(CommissionKind.FlatPerOrder, 2),
            Rule(CommissionKind.FlatPerOrder, 50, active: false),
            Rule(CommissionKind.FlatPerProduct, 0, product: NeuroQ),
        };
        Assert.Equal(3.20m, CommissionCalculator.Calculate(Facts(), rules).Sum(l => l.Amount));
    }

    [Fact]
    public void TierRule_ReplacesOnlyItsOwnGroup()
    {
        var rules = new[]
        {
            Rule(CommissionKind.FlatPerField, 10, tier: "Alpha", field: "save_method", value: "Discount"),
            Rule(CommissionKind.FlatPerField, 4, field: "save_method", value: "Discount"),
            Rule(CommissionKind.FlatPerField, 3, field: "save_method", value: "Pause"),
        };
        var alphaDiscount = CommissionCalculator.Calculate(Facts("Alpha", fields: new() { ["save_method"] = "Discount" }), rules);
        Assert.Equal(10m, Assert.Single(alphaDiscount).Amount);

        var alphaPause = CommissionCalculator.Calculate(Facts("Alpha", fields: new() { ["save_method"] = "Pause" }), rules);
        Assert.Equal(3m, Assert.Single(alphaPause).Amount);   // no Alpha rule for Pause → the general one
    }

    [Fact]
    public void Rule_Validation()
    {
        Assert.Throws<ArgumentException>(() => CommissionRule.Create(Tenant, Guid.NewGuid(), Campaign));
        Assert.Throws<ArgumentException>(() => CommissionRule.Create(Tenant, null, null));
        var r = CommissionRule.Create(Tenant, null, Campaign);
        Assert.Throws<ArgumentException>(() => r.Set("x", CommissionKind.PercentOfOrder, 101, null, null, null, null, null, true));
        Assert.Throws<ArgumentException>(() => r.Set("x", CommissionKind.FlatPerProduct, 1, null, null, null, null, null, true));
        Assert.Throws<ArgumentException>(() => r.Set("x", CommissionKind.FlatPerField, 1, null, null, "save_method", "", null, true));
        Assert.Throws<ArgumentException>(() => r.Set("x", "bonus", 1, null, null, null, null, null, true));
    }

    [Fact]
    public void Reversal_OffsetsTheEntry_AndCanOnlyHappenOnce()
    {
        var call = CallRecord.Create(Tenant, Guid.NewGuid(), Campaign);
        var line = new CommissionLine(Guid.NewGuid(), "1%", CommissionKind.PercentOfOrder, 119.85m, 1, 1.20m, "1% of $119.85");
        var earned = CommissionEntry.Earned(Tenant, call, Guid.NewGuid(), line, DateTimeOffset.UtcNow);

        var reversal = earned.Reverse("order cancelled", DateTimeOffset.UtcNow);
        Assert.True(earned.IsReversed);
        Assert.Equal(-1.20m, reversal.Amount);
        Assert.Equal(earned.Id, reversal.ReversesEntryId);
        Assert.Equal(CommissionEntryType.Reversal, reversal.EntryType);
        Assert.Throws<InvalidOperationException>(() => earned.Reverse("again", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void OrderSubmitted_KeepsTheFirstTime()
    {
        var call = CallRecord.Create(Tenant, Guid.NewGuid(), Campaign).AddInteraction(InteractionType.OrderSale);
        var first = DateTimeOffset.UtcNow.AddMinutes(-5);
        call.MarkOrderSubmitted(first);
        call.MarkOrderSubmitted(DateTimeOffset.UtcNow);
        Assert.Equal(first, call.OrderSubmittedAt);
    }

    [Theory]
    [InlineData("weekly", "2026-10-01", "2026-09-28", "2026-10-04")]
    [InlineData("biweekly", "2026-10-01", "2026-09-28", "2026-10-11")]
    [InlineData("biweekly", "2026-01-04", "2025-12-22", "2026-01-04")]   // before the anchor
    [InlineData("semimonthly", "2026-10-15", "2026-10-01", "2026-10-15")]
    [InlineData("semimonthly", "2026-02-20", "2026-02-16", "2026-02-28")]
    [InlineData("monthly", "2026-10-01", "2026-10-01", "2026-10-31")]
    public void PayPeriod_Containing(string frequency, string date, string start, string end)
    {
        var p = PayPeriods.Containing(DateOnly.Parse(date), frequency, PayPeriods.DefaultAnchor);
        Assert.Equal(DateOnly.Parse(start), p.Start);
        Assert.Equal(DateOnly.Parse(end), p.End);
    }

    [Fact]
    public void PayPeriod_PreviousAndNext_AreAdjacent()
    {
        var current = PayPeriods.Containing(new DateOnly(2026, 10, 1), PayPeriods.Biweekly, PayPeriods.DefaultAnchor);
        var previous = current.Previous(PayPeriods.Biweekly, PayPeriods.DefaultAnchor);
        Assert.Equal(current.Start.AddDays(-1), previous.End);
        Assert.Equal(current, previous.Next(PayPeriods.Biweekly, PayPeriods.DefaultAnchor));
    }

    // ── Effective dates + recalculation batches (retroactive changes) ──────────

    private static CommissionRule Dated(decimal percent, DateTimeOffset? from, DateTimeOffset? until, Guid? client = null)
    {
        var r = CommissionRule.Create(Tenant, client, client is null ? Campaign : null);
        r.Set($"{percent}%", CommissionKind.PercentOfOrder, percent, null, null, null, null, null, true, from, until);
        return r;
    }

    private static readonly DateTimeOffset Sep15 = new(2026, 9, 15, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RetroactiveRateChange_EachCallUsesTheRuleInEffectWhenItStarted()
    {
        var old = Dated(1, null, Sep15);
        var raised = Dated(2, Sep15, null);
        var rules = new[] { old, raised };

        var before = CommissionCalculator.Calculate(Facts() with { CallStart = Sep15.AddMinutes(-1) }, rules);
        var after = CommissionCalculator.Calculate(Facts() with { CallStart = Sep15 }, rules);

        Assert.Equal(1.20m, Assert.Single(before).Amount);
        Assert.Equal(2.40m, Assert.Single(after).Amount);   // the start instant is inclusive
    }

    [Fact]
    public void RulesFor_UsesCampaignRulesInEffect_ElseTheClients()
    {
        var client = Guid.NewGuid();
        var campaignLater = Dated(5, Sep15, null);
        var clientDefault = Dated(1, null, null, client);

        Assert.Same(clientDefault, Assert.Single(CommissionCalculator.RulesFor([campaignLater], [clientDefault], Sep15.AddDays(-1))));
        Assert.Same(campaignLater, Assert.Single(CommissionCalculator.RulesFor([campaignLater], [clientDefault], Sep15.AddDays(1))));
    }

    [Fact]
    public void Rule_CantEndBeforeItStarts() =>
        Assert.Throws<ArgumentException>(() => Dated(1, Sep15, Sep15));

    [Fact]
    public void RecalcBatch_Validation_AndRestartResetsCounters()
    {
        Assert.Throws<ArgumentException>(() => CommissionRecalcBatch.Create(Tenant, null, Campaign, null, Sep15, Sep15, "current", "x", null));
        Assert.Throws<ArgumentException>(() => CommissionRecalcBatch.Create(Tenant, null, Campaign, null, Sep15, Sep15.AddDays(1), "later", "x", null));
        Assert.Throws<ArgumentException>(() => CommissionRecalcBatch.Create(Tenant, null, Campaign, null, Sep15, Sep15.AddDays(1), "current", " ", null));

        var batch = CommissionRecalcBatch.Create(Tenant, null, Campaign, null, Sep15, Sep15.AddDays(1), CommissionPostTo.CallDate, "set up late", "Sue");
        batch.Start(10);
        batch.Progress(10, 4, 12.50m);
        batch.Start(10);   // a Worker restart re-runs it
        Assert.Equal(0, batch.ProcessedCalls);
        Assert.Equal(0m, batch.Difference);
        Assert.Equal(CommissionRecalcStatus.Running, batch.Status);
    }
}
