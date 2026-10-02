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
///
/// <see cref="EffectiveFrom"/> / <see cref="EffectiveUntil"/> date a rule (S171): a call is paid under the
/// rules in effect when it started, so a retroactive change ("2% from the start of this pay period") is
/// ending the old rule, adding the new one, and recalculating past calls; earlier calls keep their rate.
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
    /// <summary>Applies to calls started at or after this instant; null = always.</summary>
    public DateTimeOffset? EffectiveFrom { get; private set; }
    /// <summary>Applies to calls started before this instant; null = open-ended.</summary>
    public DateTimeOffset? EffectiveUntil { get; private set; }
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
        string? fieldName, string? fieldValue, string? tierLabel, bool isActive,
        DateTimeOffset? effectiveFrom = null, DateTimeOffset? effectiveUntil = null)
    {
        if (effectiveFrom is { } f && effectiveUntil is { } u && u <= f)
            throw new ArgumentException("A rule has to end after it starts.");
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
        EffectiveFrom = effectiveFrom;
        EffectiveUntil = effectiveUntil;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Active and in effect for a call that started at <paramref name="callStart"/>.</summary>
    public bool AppliesAt(DateTimeOffset callStart) =>
        IsActive && (EffectiveFrom is null || callStart >= EffectiveFrom) && (EffectiveUntil is null || callStart < EffectiveUntil);

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
    /// <summary>The recalculation run that wrote this entry, if any.</summary>
    public Guid? BatchId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private CommissionEntry() { }

    public static CommissionEntry Earned(
        Guid tenantId, CallRecord call, Guid agentId, CommissionLine line, DateTimeOffset at, Guid? batchId = null) => new()
    {
        BatchId = batchId,
        Id = Guid.NewGuid(), TenantId = tenantId, CallRecordId = call.Id, AgentId = agentId,
        ClientId = call.ClientId, CampaignId = call.CampaignId, EntryType = CommissionEntryType.Earned,
        RuleId = line.RuleId, RuleName = line.RuleName, Kind = line.Kind, Basis = line.Basis, Rate = line.Rate,
        Amount = line.Amount, Description = line.Description, OccurredAt = at,
    };

    /// <summary>Cancels this earned entry: marks it reversed and returns the offsetting entry.</summary>
    public CommissionEntry Reverse(string note, DateTimeOffset at, Guid? batchId = null)
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
            BatchId = batchId,
        };
    }
}

public static class CommissionEntryType
{
    public const string Earned   = "earned";
    public const string Reversal = "reversal";
}

/// <summary>
/// One "recalculate past calls" run (S171): every call in the scope started in [From, To) is recalculated
/// under the rules in effect when it started. Previewed first; applied by the Worker in chunks with
/// progress. <see cref="PostTo"/> says where corrections count: <c>current</c> (now, for periods already
/// paid) or <c>call_date</c> (back-dated to each call's start, for periods not yet paid).
/// </summary>
public class CommissionRecalcBatch
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? ClientId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public Guid? AgentId { get; private set; }
    public DateTimeOffset From { get; private set; }
    public DateTimeOffset To { get; private set; }
    public string PostTo { get; private set; } = CommissionPostTo.Current;
    public string Reason { get; private set; } = "";
    public string? RequestedBy { get; private set; }
    public string Status { get; private set; } = CommissionRecalcStatus.Pending;
    public int TotalCalls { get; private set; }
    public int ProcessedCalls { get; private set; }
    public int ChangedCalls { get; private set; }
    public decimal Difference { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private CommissionRecalcBatch() { }

    public static CommissionRecalcBatch Create(
        Guid tenantId, Guid? clientId, Guid? campaignId, Guid? agentId, DateTimeOffset from, DateTimeOffset to,
        string postTo, string reason, string? requestedBy)
    {
        if (to <= from) throw new ArgumentException("The end has to be after the start.");
        if (postTo is not (CommissionPostTo.Current or CommissionPostTo.CallDate)) throw new ArgumentException($"Unknown posting '{postTo}'.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Give a reason; it's noted on every correction.");
        return new CommissionRecalcBatch
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, CampaignId = campaignId, AgentId = agentId,
            From = from, To = to, PostTo = postTo, Reason = reason.Trim(), RequestedBy = requestedBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Begins (or, after a Worker restart, re-begins) the run — counters start over since a
    /// re-run only rewrites what still differs.</summary>
    public void Start(int totalCalls)
    {
        Status = CommissionRecalcStatus.Running;
        TotalCalls = totalCalls;
        ProcessedCalls = 0;
        ChangedCalls = 0;
        Difference = 0;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void Progress(int processed, int changed, decimal difference)
    {
        ProcessedCalls += processed;
        ChangedCalls += changed;
        Difference += difference;
    }

    public void Complete() { Status = CommissionRecalcStatus.Completed; CompletedAt = DateTimeOffset.UtcNow; }

    public void Fail(string error) { Status = CommissionRecalcStatus.Failed; Error = error; CompletedAt = DateTimeOffset.UtcNow; }
}

public static class CommissionPostTo
{
    public const string Current  = "current";
    public const string CallDate = "call_date";
}

public static class CommissionRecalcStatus
{
    public const string Pending   = "pending";
    public const string Running   = "running";
    public const string Completed = "completed";
    public const string Failed    = "failed";
}
