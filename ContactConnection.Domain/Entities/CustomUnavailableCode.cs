namespace ContactConnection.Domain.Entities;

/// <summary>
/// Admin-configured unavailable reason code shown in the agent softphone state dropdown.
/// Scoped to one or more agent roles (empty = all roles).
/// </summary>
public class CustomUnavailableCode
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string[] Roles { get; private set; } = [];  // empty = visible to all roles
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CustomUnavailableCode() { }

    public static CustomUnavailableCode Create(Guid tenantId, string name, string[] roles)
    {
        return new CustomUnavailableCode
        {
            Id        = Guid.NewGuid(),
            TenantId  = tenantId,
            Name      = name.Trim(),
            Roles     = roles,
            IsActive  = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, string[] roles)
    {
        Name  = name.Trim();
        Roles = roles;
    }

    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;
}
