namespace ContactConnection.Domain.Entities;

/// <summary>
/// A tenant-defined KPI (S181, docs/dispositions-kpi-plan.md): the share of interactions whose disposition is in the
/// <see cref="NumeratorCategoryIds"/> categories, out of those in the <see cref="DenominatorCategoryIds"/> categories
/// (empty = all interactions). E.g. Lead capture rate = Lead captured ÷ (Lead captured + Lead opportunity, not captured);
/// Transfer-to-CS rate = Transferred to CS ÷ all. Shown in the KPI widget beside the built-in KPIs.
/// </summary>
public class CustomKpi
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public List<Guid> NumeratorCategoryIds { get; private set; } = [];
    public List<Guid> DenominatorCategoryIds { get; private set; } = [];
    /// <summary><c>ratio</c> (categories ÷ categories) or <c>formula</c> (an NCalc expression over the KPI variables).</summary>
    public string Kind { get; private set; } = "ratio";
    public string? Formula { get; private set; }
    /// <summary>number / currency / percent / duration / integer.</summary>
    public string Format { get; private set; } = "percent";
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private CustomKpi() { }

    public static CustomKpi Create(Guid tenantId, string name, string? description, IEnumerable<Guid> numerator, IEnumerable<Guid> denominator,
        int displayOrder, string kind = "ratio", string? formula = null, string format = "percent")
    {
        var k = new CustomKpi { Id = Guid.NewGuid(), TenantId = tenantId, CreatedAt = DateTimeOffset.UtcNow };
        k.Update(name, description, numerator, denominator, displayOrder, kind, formula, format);
        return k;
    }

    public void Update(string name, string? description, IEnumerable<Guid> numerator, IEnumerable<Guid> denominator, int displayOrder,
        string kind = "ratio", string? formula = null, string format = "percent")
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name the KPI.");
        if (kind is not ("ratio" or "formula")) throw new ArgumentException($"Unknown KPI kind '{kind}'.");
        var num = numerator.Distinct().ToList();
        if (kind == "ratio" && num.Count == 0) throw new ArgumentException("Choose at least one category to count.");
        if (kind == "formula" && string.IsNullOrWhiteSpace(formula)) throw new ArgumentException("Enter a formula.");
        Kind = kind;
        Formula = kind == "formula" ? formula!.Trim() : null;
        Format = kind == "formula" ? format : "percent";
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        NumeratorCategoryIds = num;
        DenominatorCategoryIds = denominator.Distinct().ToList();
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
