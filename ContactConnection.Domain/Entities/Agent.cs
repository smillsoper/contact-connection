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
    public Guid? RoleId { get; private set; }
    public Role? CustomRole { get; private set; }

    public string? SipExtension { get; private set; }   // e.g. "1001" — assigned at creation, fixed for life
    public string? SipA1Hash { get; private set; }      // MD5("{ext}:{realm}:{password}") — refreshed every login
    /// <summary>IANA timezone ID (e.g. "America/Chicago"). Null means use the tenant's timezone.</summary>
    public string? Timezone { get; private set; }

    // Supervisor lock (S166) — set from Call Records "Finalize" (relieve / terminate an agent).
    // Status lock: held Unavailable, can't change status or be offered calls. SignInLocked adds:
    // signed out now, existing tokens rejected, sign-in refused — until someone unlocks.
    public DateTimeOffset? StatusLockedAt { get; private set; }
    public string? StatusLockedByName { get; private set; }
    public string? StatusLockReason { get; private set; }
    public bool SignInLocked { get; private set; }

    /// <summary>A ContactConnection support person's account in this tenant (S184) — one per person, created when they
    /// open the tenant's portal from the platform Portal. Hidden from every list, seat count and queue; never signs in
    /// with a password.</summary>
    public bool IsPlatformSupport { get; private set; }
    /// <summary>The support person's Entra object id (which person this account is).</summary>
    public string? PlatformSupportOid { get; private set; }
    public bool IsStatusLocked => StatusLockedAt is not null;

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

    /// <summary>A support person's account (S184): named "First Last (ContactConnection Support)", no usable password,
    /// no extension.</summary>
    public static Agent CreatePlatformSupport(Guid tenantId, string entraOid, string firstName, string lastName)
    {
        var a = Create(tenantId, "", "", $"support-{entraOid}@support.contactconnection.invalid", "!", AgentRole.Admin);
        a.IsPlatformSupport = true;
        a.PlatformSupportOid = entraOid;
        a.NameSupport(firstName, lastName);
        return a;
    }

    /// <summary>Keeps a support account's name in step with the person's Entra profile.</summary>
    public void NameSupport(string firstName, string lastName)
    {
        FirstName = string.IsNullOrWhiteSpace(firstName) ? "ContactConnection" : firstName.Trim();
        LastName = $"{lastName?.Trim()} (ContactConnection Support)".Trim();
    }

    public void RecordLogin() => LastLoginAt = DateTimeOffset.UtcNow;
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void SetRole(string role) => Role = role;
    public void SetCustomRole(Guid? roleId) => RoleId = roleId;

    public void UpdateProfile(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }

    public void UpdatePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void StoreMfaSecret(string secret) => MfaSecret = secret;
    public void EnableMfa() => MfaEnabled = true;
    public void ClearMfa() { MfaSecret = null; MfaEnabled = false; }

    public void SetSipCredentials(string extension, string a1hash)
    {
        SipExtension = extension;
        SipA1Hash = a1hash;
    }

    public void SetTimezone(string? ianaTimezoneId) => Timezone = ianaTimezoneId;

    /// <summary>Locks the agent's status (and, with <paramref name="signIn"/>, their sign-in). A
    /// second lock can only escalate to a sign-in lock, never relax one.</summary>
    public void Lock(string byName, string? reason, bool signIn)
    {
        StatusLockedAt     = DateTimeOffset.UtcNow;
        StatusLockedByName = byName;
        StatusLockReason   = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        SignInLocked       = SignInLocked || signIn;
    }

    public void Unlock()
    {
        StatusLockedAt     = null;
        StatusLockedByName = null;
        StatusLockReason   = null;
        SignInLocked       = false;
    }

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
