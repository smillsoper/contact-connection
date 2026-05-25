namespace ContactConnection.Domain.Entities;

/// <summary>
/// A call center agent belonging to a tenant. Stored in the tenant schema.
/// Authentication is via email + password within their tenant context.
/// </summary>
public class Agent
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;    // Unique within tenant
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = AgentRole.Agent;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public string? MfaSecret { get; private set; }
    public bool MfaEnabled { get; private set; }

    // Required by EF Core
    private Agent() { }

    public static Agent Create(
        Guid tenantId,
        string firstName,
        string lastName,
        string email,
        string passwordHash,
        string role = AgentRole.Agent)
    {
        return new Agent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FirstName = firstName,
            LastName = lastName,
            Email = email.ToLowerInvariant(),
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void RecordLogin() => LastLoginAt = DateTimeOffset.UtcNow;
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void SetRole(string role) => Role = role;

    public void UpdateProfile(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }

    public void UpdatePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void StoreMfaSecret(string secret) => MfaSecret = secret;
    public void EnableMfa() => MfaEnabled = true;
    public void ClearMfa() { MfaSecret = null; MfaEnabled = false; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public static class AgentRole
{
    public const string Agent = "agent";
    public const string Supervisor = "supervisor";
    public const string Admin = "admin";

    public static readonly IReadOnlyList<string> All = [Agent, Supervisor, Admin];
    public static bool IsValid(string role) => All.Contains(role);
}
