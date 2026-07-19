using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

/// <summary>
/// Takes an explicit tenant schema name rather than resolving it from ambient TenantContext —
/// AgentStateStore (the only writer) is a singleton with no HTTP request scope, so it never has
/// a populated TenantContext. Mirrors ICallTraceEventRepository's convention.
/// </summary>
public interface IAgentStateHistoryRepository
{
    Task AddAsync(AgentStateHistoryEntry entry, string tenantSchemaName, CancellationToken ct = default);
}
