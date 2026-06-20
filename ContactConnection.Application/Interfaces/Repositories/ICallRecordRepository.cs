using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ICallRecordRepository
{
    Task<CallRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CallRecord?> GetByIdWithInteractionsAsync(Guid id, CancellationToken ct = default);
    Task<CallRecord?> GetByContactIdExternalAsync(string contactIdExternal, CancellationToken ct = default);
    Task AddAsync(CallRecord record, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
