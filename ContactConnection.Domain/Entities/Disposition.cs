namespace ContactConnection.Domain.Entities;

/// <summary>
/// What a disposition MEANS for reporting (S181, docs/dispositions-kpi-plan.md). Every disposition maps to one category,
/// and the KPIs read the category, never the disposition text — so "Order", "Sale" and "Upsell accepted" can all be
/// sales, and moving a disposition to another category corrects every past call at once (KPIs use the current mapping).
///
/// Six built-in categories are seeded per tenant (renamable, not deletable — the KPIs reference them by <see cref="Key"/>);
/// tenants add their own ("Lead captured"…) to build custom KPIs on.
/// </summary>
public class DispositionCategory
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    /// <summary>Built-ins only (<see cref="DispositionCategoryKey"/>); null for tenant-created categories.</summary>
    public string? Key { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    /// <summary>Counts in the gross / net close-rate denominator.</summary>
    public bool SalesOpportunity { get; private set; }
    /// <summary>Left out of every KPI, the raw denominator included (test calls).</summary>
    public bool ExcludedFromKpis { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsSystem => Key is not null;

    // ── Recording retention rule (S181) — campaigns on "Always record, retain by disposition" only ──
    /// <summary><see cref="RecordingKeep"/>: keep / conversation / discard. Null = inherit (a disposition from its
    /// category; a category: keep).</summary>
    public string? RecordingAction { get; private set; }
    /// <summary>Keep this many days, overriding the campaign's retention. Null = inherit / the campaign's.</summary>
    public int? RecordingRetentionDays { get; private set; }

    public void SetRecordingRule(string? action, int? retentionDays)
    {
        if (action is not null && !RecordingKeep.IsValid(action)) throw new ArgumentException($"Unknown recording action '{action}'.");
        RecordingAction = action;
        RecordingRetentionDays = retentionDays is { } d ? Math.Clamp(d, 1, 3650) : null;
    }

    private DispositionCategory() { }

    public static DispositionCategory Create(Guid tenantId, string name, string? description, bool salesOpportunity,
        bool excludedFromKpis, int displayOrder, string? key = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Key = key, Name = name.Trim(), Description = Blank(description),
        SalesOpportunity = salesOpportunity, ExcludedFromKpis = excludedFromKpis, DisplayOrder = displayOrder,
        IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
    };

    public void Update(string name, string? description, bool salesOpportunity, bool excludedFromKpis, int displayOrder)
    {
        Name = name.Trim();
        Description = Blank(description);
        SalesOpportunity = salesOpportunity;
        ExcludedFromKpis = excludedFromKpis;
        DisplayOrder = displayOrder;
    }

    public void SetActive(bool active)
    {
        if (!active && IsSystem) throw new InvalidOperationException("Built-in categories can't be retired — rename them instead.");
        IsActive = active;
    }

    /// <summary>The six built-ins, in display order.</summary>
    public static IEnumerable<DispositionCategory> SystemDefaults(Guid tenantId) =>
    [
        Create(tenantId, "Sale", "The caller bought.", true, false, 10, DispositionCategoryKey.Sale),
        Create(tenantId, "Sales opportunity, no sale", "A real chance to sell that didn't close.", true, false, 20, DispositionCategoryKey.OpportunityNoSale),
        Create(tenantId, "Customer service", "Service calls — not a chance to sell.", false, false, 30, DispositionCategoryKey.CustomerService),
        Create(tenantId, "Junk / wrong number", "Hang-ups, wrong numbers, prank calls.", false, false, 40, DispositionCategoryKey.Junk),
        Create(tenantId, "Test call", "Left out of every KPI.", false, true, 50, DispositionCategoryKey.Test),
        Create(tenantId, "Other", null, false, false, 60, DispositionCategoryKey.Other),
    ];

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class DispositionCategoryKey
{
    public const string Sale = "sale";
    public const string OpportunityNoSale = "opportunity_no_sale";
    public const string CustomerService = "customer_service";
    public const string Junk = "junk";
    public const string Test = "test";
    public const string Other = "other";
}

/// <summary>
/// A disposition a script can record (S181) — tenant-managed, scoped like custom fields (tenant default, a client, or a
/// campaign; the narrowest scope wins for the same name), mapped to a <see cref="DispositionCategory"/>. Interactions link
/// to it by matching the recorded text against <see cref="Name"/> and <see cref="Aliases"/> (so historical wording
/// variants merge into one). <see cref="Code"/> is what a vendor file wants instead of the name.
/// </summary>
public class Disposition
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Code { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid? ClientId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public List<string> Aliases { get; private set; } = [];
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // ── Recording retention rule (S181) — campaigns on "Always record, retain by disposition" only ──
    /// <summary><see cref="RecordingKeep"/>: keep / conversation / discard. Null = inherit (a disposition from its
    /// category; a category: keep).</summary>
    public string? RecordingAction { get; private set; }
    /// <summary>Keep this many days, overriding the campaign's retention. Null = inherit / the campaign's.</summary>
    public int? RecordingRetentionDays { get; private set; }

    public void SetRecordingRule(string? action, int? retentionDays)
    {
        if (action is not null && !RecordingKeep.IsValid(action)) throw new ArgumentException($"Unknown recording action '{action}'.");
        RecordingAction = action;
        RecordingRetentionDays = retentionDays is { } d ? Math.Clamp(d, 1, 3650) : null;
    }

    /// <summary>0 = campaign, 1 = client, 2 = tenant — lower wins.</summary>
    public int ScopeRank => CampaignId is not null ? 0 : ClientId is not null ? 1 : 2;

    private Disposition() { }

    public static Disposition Create(Guid tenantId, string name, string? code, Guid categoryId, Guid? clientId, Guid? campaignId,
        IEnumerable<string>? aliases = null, int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A disposition needs a name.");
        var now = DateTimeOffset.UtcNow;
        var d = new Disposition
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = name.Trim(), Code = Blank(code), CategoryId = categoryId,
            ClientId = clientId, CampaignId = campaignId, DisplayOrder = displayOrder,
            IsActive = true, CreatedAt = now, UpdatedAt = now,
        };
        d.SetAliases(aliases ?? []);
        return d;
    }

    public void Update(string name, string? code, Guid categoryId, IEnumerable<string> aliases, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A disposition needs a name.");
        Name = name.Trim();
        Code = Blank(code);
        CategoryId = categoryId;
        DisplayOrder = displayOrder;
        SetAliases(aliases);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCategory(Guid categoryId)
    {
        CategoryId = categoryId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddAlias(string alias)
    {
        if (Matches(alias)) return;
        SetAliases([.. Aliases, alias]);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The recorded text is this disposition (its name or an alias) — trimmed, case-insensitive.</summary>
    public bool Matches(string? text)
    {
        var t = Normalize(text);
        return t.Length > 0 && (Normalize(Name) == t || Aliases.Any(a => Normalize(a) == t));
    }

    /// <summary>Whether this disposition applies to a call on <paramref name="campaignId"/> of <paramref name="clientId"/>.</summary>
    public bool AppliesTo(Guid? clientId, Guid? campaignId) =>
        (CampaignId is null || CampaignId == campaignId) && (ClientId is null || ClientId == clientId);

    public static string Normalize(string? s) => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private void SetAliases(IEnumerable<string> aliases) =>
        Aliases = aliases.Select(a => a.Trim()).Where(a => a.Length > 0 && Normalize(a) != Normalize(Name))
            .DistinctBy(Normalize).ToList();

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>What happens to a call's recording on a "retain by disposition" campaign (S181).</summary>
public static class RecordingKeep
{
    public const string Keep = "keep";
    /// <summary>Keep only from the moment the caller reached an agent.</summary>
    public const string Conversation = "conversation";
    public const string Discard = "discard";
    public static bool IsValid(string? v) => v is Keep or Conversation or Discard;
}

/// <summary>One interaction's resolved rule: the disposition's own setting, else its category's.</summary>
/// <param name="Mapped">The interaction has a catalog disposition (false = none recorded, or unmapped text).</param>
public sealed record InteractionRecordingRule(bool Mapped, string? Action, int? RetentionDays);

/// <param name="Days">How long to keep it (ignored for discard).</param>
public sealed record RecordingDecision(string Action, int Days);

/// <summary>
/// Which recording rule a call gets (S181, Stephen's rules) — pure, for "Always record, retain by disposition" campaigns:
/// <list type="number">
/// <item>Each interaction: its disposition's rule, else its category's (the caller resolves that); a mapped disposition
/// with no rule anywhere keeps the recording for the campaign's normal period.</item>
/// <item>No disposition, or unmapped text: keep for the campaign's "missing / unmapped" period.</item>
/// <item>Several interactions: keep the recording if ANY says keep (whole call beats conversation-only beats discard) —
/// one recording covers the whole call — for the LONGEST period among those that keep.</item>
/// </list>
/// </summary>
public static class RecordingRetentionPolicy
{
    public static RecordingDecision Decide(int campaignDays, int? unmappedDays, IReadOnlyCollection<InteractionRecordingRule> interactions)
    {
        var resolved = (interactions.Count == 0 ? [new InteractionRecordingRule(false, null, null)] : interactions)
            .Select(i => i.Mapped
                ? (Action: i.Action ?? RecordingKeep.Keep, Days: i.RetentionDays ?? campaignDays)
                : (Action: RecordingKeep.Keep, Days: unmappedDays ?? campaignDays))
            .ToList();
        var kept = resolved.Where(r => r.Action != RecordingKeep.Discard).ToList();
        if (kept.Count == 0) return new RecordingDecision(RecordingKeep.Discard, 0);
        var action = kept.Any(r => r.Action == RecordingKeep.Keep) ? RecordingKeep.Keep : RecordingKeep.Conversation;
        return new RecordingDecision(action, kept.Max(r => r.Days));
    }

    /// <summary>The rule an interaction's disposition gives: its own, falling back to its category's, field by field.</summary>
    public static InteractionRecordingRule ForDisposition(Disposition? disposition, DispositionCategory? category) =>
        disposition is null
            ? new InteractionRecordingRule(false, null, null)
            : new InteractionRecordingRule(true,
                disposition.RecordingAction ?? category?.RecordingAction,
                disposition.RecordingRetentionDays ?? category?.RecordingRetentionDays);
}
