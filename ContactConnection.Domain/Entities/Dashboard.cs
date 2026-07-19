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
}
