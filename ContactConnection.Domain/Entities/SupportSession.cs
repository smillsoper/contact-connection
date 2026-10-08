namespace ContactConnection.Domain.Entities;

/// <summary>Platform roles from the Portal's Entra app registration (S184).</summary>
public static class PlatformRole
{
    /// <summary>Everything in the Portal.</summary>
    public const string Owner = "owner";
    /// <summary>Tenants: open a tenant's portal and manage its settings — but not billing, usage, invoices, the card-data
    /// exports switch, provisioning, or activating / deactivating a tenant. No other Portal pages.</summary>
    public const string Support = "support";

    /// <summary>The Entra app-role values ("Platform.Owner", "Platform.Support") → our role. With no role assigned, a
    /// sign-in is refused once roles are enforced, and treated as Owner until then (so turning this on can't lock the
    /// owner out).</summary>
    public static string? Resolve(IEnumerable<string> entraRoles, bool enforce)
    {
        var roles = entraRoles.ToList();
        if (roles.Contains("Platform.Owner", StringComparer.OrdinalIgnoreCase)) return Owner;
        if (roles.Contains("Platform.Support", StringComparer.OrdinalIgnoreCase)) return Support;
        return enforce ? null : Owner;
    }
}

/// <summary>
/// A ContactConnection support person working inside a tenant's portal (S184): who, why, and for how long. Kept in the
/// platform schema; the tenant's admins see their own sessions on the Support Access page.
/// </summary>
public class SupportSession
{
    public static readonly TimeSpan Length = TimeSpan.FromMinutes(60);

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EntraOid { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string PlatformRole { get; private set; } = "";
    public string Reason { get; private set; } = "";
    /// <summary>The support person's account in the tenant.</summary>
    public Guid AgentId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    private SupportSession() { }

    public static SupportSession Start(Guid tenantId, string entraOid, string email, string name, string platformRole, string reason,
        Guid agentId, DateTimeOffset now)
    {
        var r = (reason ?? "").Trim();
        if (r.Length < 3) throw new ArgumentException("Say briefly why you're opening this tenant's portal.");
        if (r.Length > 300) r = r[..300];
        return new SupportSession
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EntraOid = entraOid, Email = email, Name = name, PlatformRole = platformRole,
            Reason = r, AgentId = agentId, StartedAt = now, ExpiresAt = now + Length,
        };
    }

    public bool IsActive(DateTimeOffset now) => EndedAt is null && now < ExpiresAt;

    /// <summary>Ends the session now (the End button). An expired session keeps its expiry as its end.</summary>
    public void End(DateTimeOffset now)
    {
        if (EndedAt is not null) return;
        EndedAt = now < ExpiresAt ? now : ExpiresAt;
    }
}
