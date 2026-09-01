using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class ScreenRecordingRepository : IScreenRecordingRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public ScreenRecordingRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<ScreenRecording?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.ScreenRecordings.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<ScreenRecording>> ListByCallRecordAsync(Guid callRecordId, CancellationToken ct = default) =>
        await Db.ScreenRecordings
            .Where(r => r.CallRecordId == callRecordId)
            .OrderBy(r => r.StartedAtServer)
            .ToListAsync(ct);

    public async Task AddAsync(ScreenRecording recording, CancellationToken ct = default) =>
        await Db.ScreenRecordings.AddAsync(recording, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
