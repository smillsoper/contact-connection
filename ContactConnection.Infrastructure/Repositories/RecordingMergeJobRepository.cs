using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class RecordingMergeJobRepository : IRecordingMergeJobRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public RecordingMergeJobRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<RecordingMergeJob?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.Set<RecordingMergeJob>().FirstOrDefaultAsync(j => j.Id == id, ct);

    public Task<RecordingMergeJob?> GetByCallRecordIdAsync(Guid callRecordId, CancellationToken ct = default) =>
        Db.Set<RecordingMergeJob>().FirstOrDefaultAsync(j => j.CallRecordId == callRecordId, ct);

    public async Task<IReadOnlyList<Guid>> FindCallRecordIdsNeedingMergeAsync(
        DateTimeOffset updatedBefore, int limit, CancellationToken ct = default)
    {
        var jobs = Db.Set<RecordingMergeJob>();

        return await Db.CallRecords
            .Where(r => r.RecordingStartedAt != null
                        && r.RecordingRetained
                        && r.RecordingUrl == null
                        && r.UpdatedAt < updatedBefore
                        && !jobs.Any(j => j.CallRecordId == r.Id))
            .OrderByDescending(r => r.RecordingStartedAt)
            .Select(r => r.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RecordingMergeJob>> GetClaimableAsync(
        DateTimeOffset now, int limit, CancellationToken ct = default) =>
        await Db.Set<RecordingMergeJob>()
            .Where(j => j.Status == RecordingMergeJobStatus.Pending && j.NextAttemptAt <= now)
            .OrderBy(j => j.NextAttemptAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task AddAsync(RecordingMergeJob job, CancellationToken ct = default) =>
        await Db.Set<RecordingMergeJob>().AddAsync(job, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
