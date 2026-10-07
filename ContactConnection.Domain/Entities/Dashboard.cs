namespace ContactConnection.Domain.Entities;

/// <summary>
/// A tenant-defined supervisor dashboard — a saved arrangement of widgets on a grid.
/// Stored in the tenant schema. Owned by the creating agent unless IsShared.
/// </summary>
public class Dashboard
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CreatedByAgentId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public bool IsShared { get; private set; }

    /// <summary>
    /// The widget layout as JSON: an array of { id, widgetType, x, y, w, h, config }.
    /// Stored as text — deserialized by the frontend, never queried by field.
    /// </summary>
    public string Layout { get; private set; } = "[]";

    /// <summary>
    /// Client dashboard (S181): shown to client users in the client portal, with a scope locked to one client and
    /// optionally some of its campaigns. Every widget on it runs inside that scope server-side, whatever its own filters
    /// say, and only <see cref="ClientWidgetTypes"/> may be placed on it.
    /// </summary>
    public bool IsClientDashboard { get; private set; }
    public Guid? ScopeClientId { get; private set; }
    /// <summary>Empty = every campaign of <see cref="ScopeClientId"/>.</summary>
    public List<Guid> ScopeCampaignIds { get; private set; } = [];

    /// <summary>Report-type widgets only — no supervisor tools (agent lists, live calls with caller numbers, callbacks).</summary>
    public static readonly IReadOnlySet<string> ClientWidgetTypes = new HashSet<string> { "kpi", "service_level_threshold", "call_state_by_campaign", "records", "chart" };

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Required by EF Core
    private Dashboard() { }

    public static Dashboard Create(
        Guid tenantId,
        Guid createdByAgentId,
        string name,
        bool isShared,
        string layout)
    {
        return new Dashboard
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenantId,
            CreatedByAgentId = createdByAgentId,
            Name             = name,
            IsShared         = isShared,
            Layout           = layout,
            CreatedAt        = DateTimeOffset.UtcNow,
            UpdatedAt        = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, bool isShared, string layout)
    {
        Name      = name;
        IsShared  = isShared;
        Layout    = layout;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetClientScope(bool isClientDashboard, Guid? clientId, IEnumerable<Guid>? campaignIds)
    {
        IsClientDashboard = isClientDashboard;
        ScopeClientId = isClientDashboard ? clientId : null;
        ScopeCampaignIds = isClientDashboard ? (campaignIds ?? []).Distinct().ToList() : [];
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// The campaigns a client-dashboard widget may report on: the scope's campaigns (or all the client's), narrowed to the
    /// widget's own campaign filter only when that campaign is inside the scope. A filter outside it is ignored, never
    /// widened to. <paramref name="clientCampaignIds"/>: every campaign of <see cref="ScopeClientId"/>.
    /// </summary>
    public IReadOnlyList<Guid> EffectiveCampaigns(IReadOnlyCollection<Guid> clientCampaignIds, Guid? widgetCampaignId)
    {
        var allowed = ScopeCampaignIds.Count == 0 ? clientCampaignIds.ToList() : ScopeCampaignIds.Where(clientCampaignIds.Contains).ToList();
        return widgetCampaignId is { } w && allowed.Contains(w) ? [w] : allowed;
    }

    /// <summary>True if this agent may see the dashboard at all — its creator, always; anyone
    /// else in the tenant only once it's shared. Mirrors IDashboardRepository.GetVisibleAsync's
    /// filter, but for a single already-fetched dashboard (the by-id GET/PUT/DELETE routes)
    /// rather than the list query.</summary>
    public bool IsVisibleTo(Guid agentId) => IsShared || CreatedByAgentId == agentId;
}
