using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Credentials;

/// <summary>Tenant-scoped ICredentialAuditService — audits ITenantCredentialStore Set/Delete.
/// Registered under the "tenant" DI key. Lazy factory pattern matching
/// TenantVersionHistoryService.</summary>
internal class TenantCredentialAuditService(ScopedTenantDbContextFactory factory) : ICredentialAuditService
{
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= factory.Create();

    public async Task RecordAsync(
        string keyName, string action, Guid actorId, string actorName, CancellationToken ct = default)
    {
        var entry = CredentialAuditEntry.Create(keyName, action, actorId, actorName);
        await Db.CredentialAuditEntries.AddAsync(entry, ct);
        await Db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CredentialAuditEntrySummary>> ListAsync(
        string keyName, CancellationToken ct = default) =>
        await Db.CredentialAuditEntries
            .Where(e => e.KeyName == keyName)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new CredentialAuditEntrySummary(e.KeyName, e.Action, e.ActorId, e.ActorName, e.CreatedAt))
            .ToListAsync(ct);
}
