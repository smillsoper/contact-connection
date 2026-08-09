namespace ContactConnection.Application.Interfaces.Services;

public record EntityVersionSummary(
    int VersionNumber,
    bool IsActive,
    Guid CreatedById,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string? ChangeSummary);

/// <summary>
/// Generic version history for any entity that opts in — Flow, TenantApiDefinition,
/// TenantApiEndpoint (tenant-scoped) and PortalApiDefinition, PortalApiEndpoint (portal-scoped).
/// Two implementations exist (resolved via keyed DI: "tenant" / "portal") since the two scopes
/// persist to different DbContexts, but the contract and semantics are identical — see
/// EntityVersion for the storage model. Callers own applying a reverted snapshot back onto the
/// live entity (GetSnapshotAsync just returns the JSON); this service only owns the history.
/// </summary>
public interface IVersionHistoryService
{
    /// <summary>Deactivates the current active version (if any) and creates a new one. Returns
    /// the new version's number.</summary>
    Task<int> SnapshotAsync(
        string entityType, Guid entityId, string snapshotJson,
        Guid actorId, string actorName, string? changeSummary = null, CancellationToken ct = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<EntityVersionSummary>> ListVersionsAsync(
        string entityType, Guid entityId, CancellationToken ct = default);

    Task<string?> GetSnapshotAsync(
        string entityType, Guid entityId, int versionNumber, CancellationToken ct = default);
}
