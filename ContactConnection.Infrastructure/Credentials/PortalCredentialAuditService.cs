using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Credentials;

/// <summary>Portal-scoped ICredentialAuditService — audits IPortalCredentialStore Set/Delete.
/// Registered under the "portal" DI key. Mirrors PortalVersionHistoryService.</summary>
internal class PortalCredentialAuditService(ContactConnectionDbContext db) : ICredentialAuditService
{
    public async Task RecordAsync(
        string keyName, string action, Guid actorId, string actorName, CancellationToken ct = default)
    {
        var entry = CredentialAuditEntry.Create(keyName, action, actorId, actorName);
        await db.CredentialAuditEntries.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CredentialAuditEntrySummary>> ListAsync(
        string keyName, CancellationToken ct = default) =>
        await db.CredentialAuditEntries
            .Where(e => e.KeyName == keyName)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new CredentialAuditEntrySummary(e.KeyName, e.Action, e.ActorId, e.ActorName, e.CreatedAt))
            .ToListAsync(ct);
}
