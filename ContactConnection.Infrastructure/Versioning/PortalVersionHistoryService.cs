using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Versioning;

/// <summary>Portal-scoped IVersionHistoryService — PortalApiDefinition, PortalApiEndpoint.
/// Registered under the "portal" DI key.</summary>
internal class PortalVersionHistoryService(ContactConnectionDbContext db) : IVersionHistoryService
{
    public async Task<int> SnapshotAsync(
        string entityType, Guid entityId, string snapshotJson,
        Guid actorId, string actorName, string? changeSummary = null, CancellationToken ct = default)
    {
        var current = await db.EntityVersions
            .Where(v => v.EntityType == entityType && v.EntityId == entityId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        current?.Deactivate();

        var nextVersion = (current?.VersionNumber ?? 0) + 1;
        var version = EntityVersion.Create(entityType, entityId, nextVersion, snapshotJson, actorId, actorName, changeSummary);
        await db.EntityVersions.AddAsync(version, ct);
        await db.SaveChangesAsync(ct);
        return nextVersion;
    }

    public async Task<IReadOnlyList<EntityVersionSummary>> ListVersionsAsync(
        string entityType, Guid entityId, CancellationToken ct = default) =>
        await db.EntityVersions
            .Where(v => v.EntityType == entityType && v.EntityId == entityId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new EntityVersionSummary(v.VersionNumber, v.IsActive, v.CreatedById, v.CreatedByName, v.CreatedAt, v.ChangeSummary))
            .ToListAsync(ct);

    public async Task<string?> GetSnapshotAsync(
        string entityType, Guid entityId, int versionNumber, CancellationToken ct = default)
    {
        var version = await db.EntityVersions.FirstOrDefaultAsync(
            v => v.EntityType == entityType && v.EntityId == entityId && v.VersionNumber == versionNumber, ct);
        return version?.SnapshotJson;
    }
}
