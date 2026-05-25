using System.Security.Cryptography;

namespace ContactConnection.Domain.Entities;

public class TenantAdminInvite
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string Role { get; private set; } = AgentRole.Admin;
    public string Token { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }

    // Navigation
    public Tenant Tenant { get; private set; } = null!;

    // Required by EF Core
    private TenantAdminInvite() { }

    public static TenantAdminInvite Create(Guid tenantId, string email, string role = AgentRole.Admin)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return new TenantAdminInvite
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email.ToLowerInvariant(),
            Role = role,
            Token = token,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        };
    }

    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    public bool IsAccepted => AcceptedAt.HasValue;
    public void Accept() => AcceptedAt = DateTimeOffset.UtcNow;
}
