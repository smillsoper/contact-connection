using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class CallbackRepository : ICallbackRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public CallbackRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<Callback?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.Callbacks.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Callback>> ListByCallRecordAsync(Guid callRecordId, CancellationToken ct = default) =>
        await Db.Callbacks
            .Where(c => c.CallRecordId == callRecordId)
            .OrderByDescending(c => c.RequestedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Callback>> ListByCampaignAsync(
        Guid campaignId, string? status, int limit, CancellationToken ct = default)
    {
        var q = Db.Callbacks.Where(c => c.CampaignId == campaignId);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(c => c.Status == status);

        return await q.OrderByDescending(c => c.RequestedAt).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Callback>> ListPendingByNumberAsync(
        Guid campaignId, string callbackNumber, CancellationToken ct = default)
    {
        var number = callbackNumber.Trim();
        return await Db.Callbacks
            .Where(c => c.CampaignId == campaignId
                        && c.CallbackNumber == number
                        && (c.Status == CallbackStatus.Requested || c.Status == CallbackStatus.Scheduled))
            .OrderBy(c => c.RequestedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Callback callback, CancellationToken ct = default) =>
        await Db.Callbacks.AddAsync(callback, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
