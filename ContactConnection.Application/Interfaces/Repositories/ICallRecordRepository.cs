using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ICallRecordRepository
{
    Task<CallRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CallRecord?> GetByIdWithInteractionsAsync(Guid id, CancellationToken ct = default);
    Task<CallRecord?> GetByContactIdExternalAsync(string contactIdExternal, CancellationToken ct = default);
    Task AddAsync(CallRecord record, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Ids of call records that still hold a recording (<c>RecordingRetained</c> and a start
    /// timestamp), oldest call first — the retention purge sweep pulls a batch and applies each
    /// record's campaign-specific <c>RecordingRetentionDays</c> in memory before deleting.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindRetainedRecordingIdsOldestFirstAsync(int limit, CancellationToken ct = default);
}
