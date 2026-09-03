using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class ScheduledCallbackRepository : IScheduledCallbackRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public ScheduledCallbackRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<ScheduledCallback?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.ScheduledCallbacks.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<ScheduledCallback>> ListByCallRecordAsync(Guid callRecordId, CancellationToken ct = default) =>
        await Db.ScheduledCallbacks
            .Where(c => c.CallRecordId == callRecordId)
            .OrderByDescending(c => c.RequestedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ScheduledCallback>> ListByCampaignAsync(
        Guid campaignId, string? status, int limit, CancellationToken ct = default)
    {
        var q = Db.ScheduledCallbacks.Where(c => c.CampaignId == campaignId);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(c => c.Status == status);

        return await q.OrderByDescending(c => c.RequestedAt).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ScheduledCallback>> ListPendingByNumberAsync(
        Guid campaignId, string callbackNumber, CancellationToken ct = default)
    {
        var number = callbackNumber.Trim();
        return await Db.ScheduledCallbacks
            .Where(c => c.CampaignId == campaignId
                        && c.CallbackNumber == number
                        && c.Status == ScheduledCallbackStatus.Scheduled)
            .OrderBy(c => c.RequestedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(ScheduledCallback callback, CancellationToken ct = default) =>
        await Db.ScheduledCallbacks.AddAsync(callback, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
