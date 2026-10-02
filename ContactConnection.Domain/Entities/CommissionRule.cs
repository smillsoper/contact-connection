namespace ContactConnection.Domain.Entities;

/// <summary>
/// How an agent earns commission on a call (S171). Scoped to a campaign, or to a client as the default
/// for its campaigns — a campaign with any active rules uses only its own; otherwise its client's.
///
/// <list type="bullet">
/// <item><b>percent_of_order</b> — <see cref="Amount"/>% of the order total less shipping, tax and fees
/// (the NeuroQ V1 basis).</item>
/// <item><b>flat_per_order</b> — $<see cref="Amount"/> once per submitted order.</item>
/// <item><b>flat_per_product</b> — $<see cref="Amount"/> per unit of <see cref="ProductId"/> in the order.</item>
/// <item><b>flat_per_field</b> — $<see cref="Amount"/> when the call's custom field <see cref="FieldName"/>
/// equals <see cref="FieldValue"/> (script-set flags, e.g. a retention call's save method). Applies
/// with or without an order.</item>
/// </list>
///
/// <see cref="TierLabel"/> limits a rule to calls won through that routing tier ("Alpha"). A matching
/// tier rule REPLACES the general rules of the same kind and target for that call — so "10% · Alpha" plus
/// "1%" pays Alpha calls 10% and every other call 1%. See <see cref="CommissionCalculator"/>.
/// </summary>
public class CommissionRule
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? ClientId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public string Name { get; private set; } = "";
    public string Kind { get; private set; } = CommissionKind.PercentOfOrder;
    /// <summary>Percent for percent_of_order; dollars for the flat kinds.</summary>
    public decimal Amount { get; private set; }
    public Guid? ProductId { get; private set; }
    /// <summary>The product's SKU/description when the rule was saved — for display only.</summary>
    public string? ProductLabel { get; private set; }
    public string? FieldName { get; private set; }
    public string? FieldValue { get; private set; }
    public string? TierLabel { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private CommissionRule() { }

    public static CommissionRule Create(Guid tenantId, Guid? clientId, Guid? campaignId)
    {
        if ((clientId is null) == (campaignId is null))
            throw new ArgumentException("A commission rule belongs to either a client or a campaign.");
        var now = DateTimeOffset.UtcNow;
        return new CommissionRule
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, CampaignId = campaignId,
            CreatedAt = now, UpdatedAt = now,
        };
    }

    public void Set(
        string name, string kind, decimal amount, Guid? productId, string? productLabel,
        string? fieldName, string? fieldValue, string? tierLabel, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.");
        if (!CommissionKind.IsValid(kind)) throw new ArgumentException($"Unknown commission kind '{kind}'.");
        if (amount < 0) throw new ArgumentException("Amount can't be negative.");
        if (kind == CommissionKind.PercentOfOrder && amount > 100) throw new ArgumentException("A percentage can't exceed 100.");
        if (kind == CommissionKind.FlatPerProduct && productId is null) throw new ArgumentException("Choose a product.");
        if (kind == CommissionKind.FlatPerField && (string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(fieldValue)))
            throw new ArgumentException("Choose a custom field and the value that earns the commission.");

        Name = name.Trim();
        Kind = kind;
        Amount = amount;
        ProductId = kind == CommissionKind.FlatPerProduct ? productId : null;
        ProductLabel = kind == CommissionKind.FlatPerProduct ? Blank(productLabel) : null;
        FieldName = kind == CommissionKind.FlatPerField ? fieldName!.Trim() : null;
        FieldValue = kind == CommissionKind.FlatPerField ? fieldValue!.Trim() : null;
        TierLabel = Blank(tierLabel);
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class CommissionKind
{
    public const string PercentOfOrder = "percent_of_order";
    public const string FlatPerOrder   = "flat_per_order";
    public const string FlatPerProduct = "flat_per_product";
    public const string FlatPerField   = "flat_per_field";

    public static bool IsValid(string? v) => v is PercentOfOrder or FlatPerOrder or FlatPerProduct or FlatPerField;
    public static bool NeedsOrder(string v) => v != FlatPerField;
}

/// <summary>
/// One line in an agent's commission ledger (S171). Entries are never edited or deleted: when a call's
/// commission changes (order resubmitted with a different cart, a flag edited, an order cancelled) each
/// earned entry still in force gets a <see cref="CommissionEntryType.Reversal"/> entry for the negative
/// amount, and the new result is written as fresh earned entries. Pay periods sum entries by
/// <see cref="OccurredAt"/>, so a change after a period closed lands in the current one.
/// </summary>
public class CommissionEntry
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CallRecordId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid CampaignId { get; private set; }
    public string EntryType { get; private set; } = CommissionEntryType.Earned;
    public Guid? RuleId { get; private set; }
    public string RuleName { get; private set; } = "";
    public string Kind { get; private set; } = "";
    /// <summary>What the rule was applied to: the order amount (percent), units (per product), else 1.</summary>
    public decimal Basis { get; private set; }
    /// <summary>The rule's percent or dollar amount at the time.</summary>
    public decimal Rate { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = "";
    /// <summary>Earned entries: set once a reversal cancels this entry.</summary>
    public bool IsReversed { get; private set; }
    public Guid? ReversesEntryId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private CommissionEntry() { }

    public static CommissionEntry Earned(
        Guid tenantId, CallRecord call, Guid agentId, CommissionLine line, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, CallRecordId = call.Id, AgentId = agentId,
        ClientId = call.ClientId, CampaignId = call.CampaignId, EntryType = CommissionEntryType.Earned,
        RuleId = line.RuleId, RuleName = line.RuleName, Kind = line.Kind, Basis = line.Basis, Rate = line.Rate,
        Amount = line.Amount, Description = line.Description, OccurredAt = at,
    };

    /// <summary>Cancels this earned entry: marks it reversed and returns the offsetting entry.</summary>
    public CommissionEntry Reverse(string note, DateTimeOffset at)
    {
        if (EntryType != CommissionEntryType.Earned || IsReversed)
            throw new InvalidOperationException("Only an earned entry still in force can be reversed.");
        IsReversed = true;
        return new CommissionEntry
        {
            Id = Guid.NewGuid(), TenantId = TenantId, CallRecordId = CallRecordId, AgentId = AgentId,
            ClientId = ClientId, CampaignId = CampaignId, EntryType = CommissionEntryType.Reversal,
            RuleId = RuleId, RuleName = RuleName, Kind = Kind, Basis = Basis, Rate = Rate,
            Amount = -Amount, Description = Description, ReversesEntryId = Id, Note = note, OccurredAt = at,
        };
    }
}

public static class CommissionEntryType
{
    public const string Earned   = "earned";
    public const string Reversal = "reversal";
}
