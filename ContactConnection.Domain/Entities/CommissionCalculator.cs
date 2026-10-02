using ContactConnection.Domain.ValueObjects.Commerce;

namespace ContactConnection.Domain.Entities;

/// <summary>What a call earned under one rule.</summary>
public record CommissionLine(Guid RuleId, string RuleName, string Kind, decimal Basis, decimal Rate, decimal Amount, string Description);

/// <summary>The parts of a call that commission rules read.</summary>
public record CommissionCallFacts(
    bool OrderSubmitted,
    CartDocument? Cart,
    string? TierLabel,
    IReadOnlyDictionary<string, string> CustomFields);

/// <summary>
/// Pure commission math (S171). Rules are grouped by what they pay on — the kind plus its target
/// (product, or field + value). Within a group, rules limited to the call's routing tier replace the
/// general (no-tier) rules; rules for another tier never apply. Order kinds need a submitted order with
/// items. Amounts round to cents, half away from zero.
/// </summary>
public static class CommissionCalculator
{
    public static List<CommissionLine> Calculate(CommissionCallFacts facts, IEnumerable<CommissionRule> rules)
    {
        var lines = new List<CommissionLine>();
        var applicable = rules
            .Where(r => r.IsActive)
            .Where(r => r.TierLabel is null || string.Equals(r.TierLabel, facts.TierLabel, StringComparison.OrdinalIgnoreCase));

        foreach (var group in applicable.GroupBy(GroupKey))
        {
            var tiered = group.Where(r => r.TierLabel is not null).ToList();
            foreach (var rule in tiered.Count > 0 ? tiered : group.ToList())
                if (Apply(rule, facts) is { } line && line.Amount != 0) lines.Add(line);
        }
        return lines;
    }

    /// <summary>The order amount commission is a percentage of: total less shipping, tax and fees.</summary>
    public static decimal OrderBasis(CartDocument cart) =>
        Math.Max(0, cart.CartTotal - cart.Shipping - cart.SalesTax - (cart.Fees?.Sum(f => f.Amount) ?? 0));

    private static string GroupKey(CommissionRule r) => r.Kind switch
    {
        CommissionKind.FlatPerProduct => $"{r.Kind}:{r.ProductId}",
        CommissionKind.FlatPerField   => $"{r.Kind}:{r.FieldName?.ToLowerInvariant()}={r.FieldValue?.ToLowerInvariant()}",
        _                             => r.Kind,
    };

    private static CommissionLine? Apply(CommissionRule r, CommissionCallFacts f)
    {
        var hasOrder = f.OrderSubmitted && f.Cart is { Items.Count: > 0 };
        if (CommissionKind.NeedsOrder(r.Kind) && !hasOrder) return null;
        var tier = r.TierLabel is null ? "" : $" ({r.TierLabel})";

        switch (r.Kind)
        {
            case CommissionKind.PercentOfOrder:
            {
                var basis = OrderBasis(f.Cart!);
                return Line(r, basis, basis * r.Amount / 100m, $"{r.Amount:0.##}% of {Money(basis)}{tier}");
            }
            case CommissionKind.FlatPerOrder:
                return Line(r, 1, r.Amount, $"{Money(r.Amount)} per order{tier}");
            case CommissionKind.FlatPerProduct:
            {
                var units = f.Cart!.Items.Where(i => i.ProductId == r.ProductId).Sum(i => i.Quantity);
                if (units <= 0) return null;
                var what = r.ProductLabel ?? f.Cart.Items.First(i => i.ProductId == r.ProductId).Sku;
                return Line(r, units, units * r.Amount, $"{units} × {what} at {Money(r.Amount)}{tier}");
            }
            case CommissionKind.FlatPerField:
            {
                var matches = f.CustomFields.TryGetValue(r.FieldName!, out var value)
                    && string.Equals(value?.Trim(), r.FieldValue, StringComparison.OrdinalIgnoreCase);
                return matches ? Line(r, 1, r.Amount, $"{r.FieldName} = {r.FieldValue}{tier}") : null;
            }
            default:
                return null;
        }
    }

    private static string Money(decimal d) => "$" + d.ToString("#,0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static CommissionLine Line(CommissionRule r, decimal basis, decimal amount, string description) =>
        new(r.Id, r.Name, r.Kind, basis, r.Amount, Math.Round(amount, 2, MidpointRounding.AwayFromZero), description);
}
