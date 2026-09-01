using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

/// <summary>
/// Tenant-scoped persistence for <see cref="RecordingMergeJob"/> rows. Resolves the schema from
/// the ambient <c>TenantContext</c> (the worker sets it per tenant before each poll pass), same
/// pattern as <see cref="IScreenRecordingRepository"/>.
/// </summary>
public interface IRecordingMergeJobRepository
{
    Task<RecordingMergeJob?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RecordingMergeJob?> GetByCallRecordIdAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>
    /// Call record ids that have a recording (<c>recording_started_at</c> set), are still retained,
    /// have no merged output yet (<c>recording_url</c> null), have settled (<c>updated_at</c> before
    /// <paramref name="updatedBefore"/> — lets late recording events land first), and don't already
    /// have a merge job. Newest first, capped at <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindCallRecordIdsNeedingMergeAsync(
        DateTimeOffset updatedBefore, int limit, CancellationToken ct = default);

    /// <summary>Pending jobs whose backoff has elapsed, oldest first, capped at <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<RecordingMergeJob>> GetClaimableAsync(
        DateTimeOffset now, int limit, CancellationToken ct = default);

    Task AddAsync(RecordingMergeJob job, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
