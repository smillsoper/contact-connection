namespace ContactConnection.Domain.Entities;

/// <summary>
/// A flow definition — the JSON graph that drives agent scripting or telephony routing.
/// Stored in the tenant schema. Immutable once published; new edits produce a new version.
/// </summary>
public class Flow
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? ClientId { get; private set; }      // null = available to all clients in tenant
    public Guid? CampaignId { get; private set; }    // null = available to all campaigns for client

    public string Name { get; private set; } = string.Empty;
    public string FlowType { get; private set; } = Entities.FlowType.Crm;  // crm | telephony

    // Telephony flows only: routing direction and dial-mode sub-type
    public string? FlowDirection { get; private set; }   // inbound | outbound
    public string? FlowSubType   { get; private set; }   // manual | progressive | predictive

    public int Version { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// The full flow graph as JSON. Schema: { entry_node, nodes: { [id]: { type, ... } } }
    /// Stored as text — deserialized at engine load time, never queried by field.
    /// </summary>
    public string Definition { get; private set; } = "{}";

    // ── Draft / published (S183) ──────────────────────────────────────────────
    // Definition above is the DRAFT — what the designer saves. Agents, live calls and training runs get the published
    // copy below; saving never changes what's live until Publish(). A designer sandbox can run either.

    /// <summary>The live script — null until the flow is first published.</summary>
    public string? PublishedDefinition { get; private set; }
    /// <summary>Which draft <see cref="Version"/> was last published.</summary>
    public int? PublishedVersion { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>The draft has been saved since it was last published (or was never published).</summary>
    public bool HasUnpublishedChanges => PublishedVersion != Version;

    /// <summary>The script a run uses: the draft (designer sandbox only) or the published copy.</summary>
    public string DefinitionFor(bool draft) => draft ? Definition : PublishedDefinition ?? "{}";
    /// <summary>The version number that script carries.</summary>
    public int VersionFor(bool draft) => draft ? Version : PublishedVersion ?? Version;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid CreatedByAgentId { get; private set; }

    // Required by EF Core
    private Flow() { }

    public static Flow Create(
        Guid tenantId,
        Guid createdByAgentId,
        string name,
        string flowType,
        string definition,
        Guid? clientId = null,
        Guid? campaignId = null,
        string? flowDirection = null,
        string? flowSubType = null)
    {
        return new Flow
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            CampaignId = campaignId,
            CreatedByAgentId = createdByAgentId,
            Name = name,
            FlowType = flowType,
            FlowDirection = flowType == Entities.FlowType.Telephony ? flowDirection : null,
            FlowSubType   = flowType == Entities.FlowType.Telephony ? flowSubType   : null,
            Version = 1,
            IsActive = false,   // flows start as drafts; explicitly published
            Definition = definition,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>Make the current draft the live script (and the flow available to agents and calls).</summary>
    public void Publish()
    {
        PublishedDefinition = Definition;
        PublishedVersion = Version;
        PublishedAt = DateTimeOffset.UtcNow;
        IsActive = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Rename(string name)
    {
        Name = name;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateDefinition(string definition)
    {
        Definition = definition;
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// The flow's home client/campaign — optional (both null = shared, e.g. a sub-flow reused by
    /// several campaigns). Never decides a live call's campaign (that always comes from the call);
    /// it scopes the flow and gives previews started from the agent portal a realistic campaign.
    /// A campaign without its client is invalid.
    /// </summary>
    public void SetScope(Guid? clientId, Guid? campaignId)
    {
        if (campaignId is not null && clientId is null)
            throw new ArgumentException("A campaign scope needs its client.", nameof(clientId));
        ClientId   = clientId;
        CampaignId = campaignId;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    public void UpdateMetadata(string? flowDirection, string? flowSubType)
    {
        if (FlowType != Entities.FlowType.Telephony) return;
        FlowDirection = flowDirection;
        FlowSubType   = flowSubType;
        UpdatedAt     = DateTimeOffset.UtcNow;
    }
}

public static class FlowType
{
    public const string Crm = "crm";
    public const string Telephony = "telephony";

    public static readonly IReadOnlyList<string> All = [Crm, Telephony];
    public static bool IsValid(string type) => All.Contains(type);
}

public static class FlowTelephonyDirection
{
    public const string Inbound  = "inbound";
    public const string Outbound = "outbound";
}

public static class FlowTelephonySubType
{
    public const string Manual      = "manual";
    public const string Progressive = "progressive";
    public const string Predictive  = "predictive";

    public static readonly IReadOnlyList<string> All = [Manual, Progressive, Predictive];
}
