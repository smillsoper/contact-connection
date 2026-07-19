using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;

namespace ContactConnection.Infrastructure.Repositories;

/// <summary>
/// Uses ITenantDbContextFactory directly (explicit schema per call) — consumed by
/// AgentStateStore, a singleton with no HTTP request scope. Mirrors CallTraceEventRepository.
/// </summary>
public class AgentStateHistoryRepository(ITenantDbContextFactory factory) : IAgentStateHistoryRepository
{
    public async Task AddAsync(AgentStateHistoryEntry entry, string tenantSchemaName, CancellationToken ct = default)
    {
        await using var db = factory.Create(tenantSchemaName);
        await db.AgentStateHistory.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);
    }
}
