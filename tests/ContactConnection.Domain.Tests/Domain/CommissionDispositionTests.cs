using ContactConnection.Domain.Entities;
using Xunit;

namespace ContactConnection.Domain.Tests.Domain;

/// <summary>flat_per_disposition (S181): pays per interaction on a catalog disposition or reporting category.</summary>
public class CommissionDispositionTests
{
    private static readonly Guid RetentionSave = Guid.NewGuid();
    private static readonly Guid Cancelled = Guid.NewGuid();
    private static readonly Guid SavesCategory = Guid.NewGuid();
    private static readonly Guid CsCategory = Guid.NewGuid();

    private static CommissionRule Rule(decimal amount, Guid? disposition = null, Guid? category = null, string? tier = null)
    {
        var r = CommissionRule.Create(Guid.NewGuid(), null, Guid.NewGuid());
        r.Set("Save bonus", CommissionKind.FlatPerDisposition, amount, null, null, null, null, tier, true,
            dispositionId: disposition, dispositionCategoryId: category, dispositionLabel: "Retention save");
        return r;
    }

    private static CommissionCallFacts Facts(Guid? disposition, Guid? category, string? tier = null) =>
        new(false, null, tier, new Dictionary<string, string>(), null, disposition, category);

    [Fact]
    public void PaysOnThatDisposition_WithoutAnOrder()
    {
        var lines = CommissionCalculator.Calculate(Facts(RetentionSave, SavesCategory), [Rule(2m, disposition: RetentionSave)]);
        Assert.Equal(2m, Assert.Single(lines).Amount);
        Assert.Empty(CommissionCalculator.Calculate(Facts(Cancelled, CsCategory), [Rule(2m, disposition: RetentionSave)]));
        Assert.Empty(CommissionCalculator.Calculate(Facts(null, null), [Rule(2m, disposition: RetentionSave)]));
    }

    [Fact]
    public void CategoryRule_PaysOnAnyDispositionInTheCategory()
    {
        var rule = Rule(1.5m, category: SavesCategory);
        Assert.Single(CommissionCalculator.Calculate(Facts(RetentionSave, SavesCategory), [rule]));
        // Recategorized: the same disposition now in CS no longer earns.
        Assert.Empty(CommissionCalculator.Calculate(Facts(RetentionSave, CsCategory), [rule]));
    }

    [Fact]
    public void TierRuleReplacesTheGeneralRule_ForTheSameDisposition()
    {
        var rules = new[] { Rule(2m, disposition: RetentionSave), Rule(5m, disposition: RetentionSave, tier: "Alpha") };
        Assert.Equal(5m, Assert.Single(CommissionCalculator.Calculate(Facts(RetentionSave, SavesCategory, "Alpha"), rules)).Amount);
        Assert.Equal(2m, Assert.Single(CommissionCalculator.Calculate(Facts(RetentionSave, SavesCategory, null), rules)).Amount);
    }

    [Fact]
    public void Validation_NeedsExactlyOneTarget()
    {
        var r = CommissionRule.Create(Guid.NewGuid(), null, Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => r.Set("x", CommissionKind.FlatPerDisposition, 1, null, null, null, null, null, true));
        Assert.Throws<ArgumentException>(() => r.Set("x", CommissionKind.FlatPerDisposition, 1, null, null, null, null, null, true,
            dispositionId: RetentionSave, dispositionCategoryId: SavesCategory));
        Assert.False(CommissionKind.NeedsOrder(CommissionKind.FlatPerDisposition));
    }
}
