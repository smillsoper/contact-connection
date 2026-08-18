namespace ContactConnection.Domain.Entities;

/// <summary>Which write happened to a credential — see CredentialAuditEntry.</summary>
public static class CredentialAuditAction
{
    public const string Set = "set";
    public const string Delete = "delete";
}

/// <summary>
/// Records THAT a credential was changed — actor, timestamp, key name, action — and deliberately
/// never the secret value itself (see API_HARDENING_CHECKLIST.md Tier 1, credential audit trail).
/// Every Set/Delete against ITenantCredentialStore/IPortalCredentialStore appends a new row here;
/// nothing is ever mutated or deleted, so the full history of "who touched this key and when" is
/// always retained.
///
/// Lives in the same schema as the store it audits — TenantDbContext (per-tenant schema) for
/// tenant credentials, ContactConnectionDbContext (public schema) for portal credentials —
/// mirroring how EntityVersion is scoped for Flow/Definition/Endpoint history. Unlike
/// EntityVersion, there is no snapshot payload and no "active" flag: this is a pure append-only
/// log, not a revertible version chain (there is nothing to revert TO — the actual secret value
/// only ever lives in Key Vault).
/// </summary>
public class CredentialAuditEntry
{
    public Guid Id { get; private set; }
    public string KeyName { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private CredentialAuditEntry() { }

    public static CredentialAuditEntry Create(
        string keyName, string action, Guid actorId, string actorName) => new()
    {
        Id = Guid.NewGuid(),
        KeyName = keyName,
        Action = action,
        ActorId = actorId,
        ActorName = actorName,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
