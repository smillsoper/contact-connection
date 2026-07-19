using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;

namespace ContactConnection.Infrastructure.Repositories;

public class CallStateHistoryRepository(ITenantDbContextFactory factory) : ICallStateHistoryRepository
{
    public async Task AddAsync(CallStateHistoryEntry entry, string tenantSchemaName, CancellationToken ct = default)
    {
        await using var db = factory.Create(tenantSchemaName);
        await db.CallStateHistory.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);
    }
}
