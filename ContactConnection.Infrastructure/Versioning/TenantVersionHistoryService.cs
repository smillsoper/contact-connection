using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Versioning;

/// <summary>Tenant-scoped IVersionHistoryService — Flow, TenantApiDefinition,
/// TenantApiEndpoint. Registered under the "tenant" DI key. Lazy factory pattern matching the
/// existing tenant repositories (e.g. TenantApiDefinitionRepository).</summary>
internal class TenantVersionHistoryService(ScopedTenantDbContextFactory factory) : IVersionHistoryService
{
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= factory.Create();

    public async Task<int> SnapshotAsync(
        string entityType, Guid entityId, string snapshotJson,
        Guid actorId, string actorName, string? changeSummary = null, CancellationToken ct = default)
    {
        var current = await Db.EntityVersions
            .Where(v => v.EntityType == entityType && v.EntityId == entityId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        current?.Deactivate();

        var nextVersion = (current?.VersionNumber ?? 0) + 1;
        var version = EntityVersion.Create(entityType, entityId, nextVersion, snapshotJson, actorId, actorName, changeSummary);
        await Db.EntityVersions.AddAsync(version, ct);
        await Db.SaveChangesAsync(ct);
        return nextVersion;
    }

    public async Task<IReadOnlyList<EntityVersionSummary>> ListVersionsAsync(
        string entityType, Guid entityId, CancellationToken ct = default) =>
        await Db.EntityVersions
            .Where(v => v.EntityType == entityType && v.EntityId == entityId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new EntityVersionSummary(v.VersionNumber, v.IsActive, v.CreatedById, v.CreatedByName, v.CreatedAt, v.ChangeSummary))
            .ToListAsync(ct);

    public async Task<string?> GetSnapshotAsync(
        string entityType, Guid entityId, int versionNumber, CancellationToken ct = default)
    {
        var version = await Db.EntityVersions.FirstOrDefaultAsync(
            v => v.EntityType == entityType && v.EntityId == entityId && v.VersionNumber == versionNumber, ct);
        return version?.SnapshotJson;
    }
}
