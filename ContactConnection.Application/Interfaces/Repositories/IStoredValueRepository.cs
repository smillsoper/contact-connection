using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IStoredValueRepository
{
    Task<StoredValue?> GetAsync(Guid tenantId, string scope, Guid scopeId, string keyName, CancellationToken ct = default);
    Task AddAsync(StoredValue value, CancellationToken ct = default);

    /// <summary>Oldest-expired-first ids for the retention sweep — same batching shape as
    /// ICallRecordRepository.FindWithSensitiveDataOldestFirstAsync.</summary>
    Task<IReadOnlyList<Guid>> FindExpiredOldestFirstAsync(int limit, CancellationToken ct = default);
    Task<StoredValue?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Remove(StoredValue value);

    Task SaveChangesAsync(CancellationToken ct = default);
}
