using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ICallStateHistoryRepository
{
    Task AddAsync(CallStateHistoryEntry entry, string tenantSchemaName, CancellationToken ct = default);
}
