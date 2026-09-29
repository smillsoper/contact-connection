using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class CallRecordAuditRepository(ScopedTenantDbContextFactory factory) : ICallRecordAuditRepository
{
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= factory.Create();

    public async Task<IReadOnlyList<CallRecordAuditEntry>> GetByCallRecordAsync(Guid callRecordId, CancellationToken ct = default) =>
        await Db.CallRecordAuditEntries.AsNoTracking()
            .Where(e => e.CallRecordId == callRecordId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(CallRecordAuditEntry entry, CancellationToken ct = default)
    {
        await Db.CallRecordAuditEntries.AddAsync(entry, ct);
        await Db.SaveChangesAsync(ct);
    }
}
