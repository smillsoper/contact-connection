namespace ContactConnection.Domain.Entities;

public class Client
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? AccountNumber { get; private set; }
    public string Status { get; private set; } = ClientStatus.Active;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    private readonly List<Campaign> _campaigns = [];
    public IReadOnlyList<Campaign> Campaigns => _campaigns.AsReadOnly();

    private Client() { }

    public static Client Create(Guid tenantId, string name, string? accountNumber = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Client
        {
            Id            = Guid.NewGuid(),
            TenantId      = tenantId,
            Name          = name.Trim(),
            AccountNumber = accountNumber?.Trim(),
            Status        = ClientStatus.Active,
            CreatedAt     = now,
            UpdatedAt     = now
        };
    }

    public void Update(string name, string? accountNumber)
    {
        Name          = name.Trim();
        AccountNumber = accountNumber?.Trim();
        UpdatedAt     = DateTimeOffset.UtcNow;
    }

    public void Activate()   { Status = ClientStatus.Active;   UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { Status = ClientStatus.Inactive; UpdatedAt = DateTimeOffset.UtcNow; }
}

public static class ClientStatus
{
    public const string Active   = "active";
    public const string Inactive = "inactive";
}
