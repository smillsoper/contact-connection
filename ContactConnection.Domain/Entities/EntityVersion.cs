namespace ContactConnection.Domain.Entities;

/// <summary>Which entity types opt into version history — see EntityVersion.</summary>
public static class VersionedEntityType
{
    public const string Flow = "flow";
    public const string TenantApiDefinition = "tenant_api_definition";
    public const string TenantApiEndpoint = "tenant_api_endpoint";
    public const string PortalApiDefinition = "portal_api_definition";
    public const string PortalApiEndpoint = "portal_api_endpoint";
}

/// <summary>
/// A full point-in-time snapshot of some other entity's state. Every write to a versioned entity
/// creates a NEW row here — nothing is ever mutated or deleted, so the complete history is always
/// retained (see API_HARDENING_CHECKLIST.md Tier 1). Exactly one version is "active" per
/// (EntityType, EntityId) at a time; reverting to an older version creates yet another new
/// version carrying that old snapshot forward, rather than rewinding/discarding anything.
///
/// EntityType + EntityId together identify the versioned entity (no FK — this table is
/// deliberately generic so any entity can opt in without a schema change here). CreatedById/
/// CreatedByName are denormalized at write time so history reads correctly even if the acting
/// agent/admin is later renamed or removed. VersionNumber 1's CreatedBy* is "who created it";
/// the active version's CreatedBy* is "who last edited it" — both fall out of this table without
/// needing separate fields on the parent entity.
/// </summary>
public class EntityVersion
{
    public Guid Id { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public int VersionNumber { get; private set; }
    public string SnapshotJson { get; private set; } = "{}";
    public bool IsActive { get; private set; }
    public Guid CreatedById { get; private set; }
    public string CreatedByName { get; private set; } = string.Empty;
    public string? ChangeSummary { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private EntityVersion() { }

    public static EntityVersion Create(
        string entityType, Guid entityId, int versionNumber, string snapshotJson,
        Guid createdById, string createdByName, string? changeSummary) => new()
    {
        Id = Guid.NewGuid(),
        EntityType = entityType,
        EntityId = entityId,
        VersionNumber = versionNumber,
        SnapshotJson = snapshotJson,
        IsActive = true,
        CreatedById = createdById,
        CreatedByName = createdByName,
        ChangeSummary = changeSummary,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void Deactivate() => IsActive = false;
}
