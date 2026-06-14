using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class PhoneNumberRoutingRepository : IPhoneNumberRoutingRepository
{
    private readonly ContactConnectionDbContext _db;

    public PhoneNumberRoutingRepository(ContactConnectionDbContext db) => _db = db;

    public Task<PhoneNumberRouting?> GetByNumberAsync(string number, CancellationToken ct = default) =>
        _db.PhoneNumberRoutings.FirstOrDefaultAsync(p => p.Number == number, ct);

    public async Task UpsertAsync(
        string number, Guid tenantId, Guid campaignId, bool isActive = true,
        CancellationToken ct = default)
    {
        var existing = await _db.PhoneNumberRoutings
            .FirstOrDefaultAsync(p => p.Number == number, ct);

        if (existing is null)
        {
            var routing = PhoneNumberRouting.Create(number, tenantId, campaignId);
            if (!isActive) routing.Deactivate();
            await _db.PhoneNumberRoutings.AddAsync(routing, ct);
        }
        else
        {
            existing.Update(campaignId);
            if (isActive) existing.Activate();
            else existing.Deactivate();
        }
    }

    public async Task SetActiveAsync(string number, bool isActive, CancellationToken ct = default)
    {
        var routing = await _db.PhoneNumberRoutings
            .FirstOrDefaultAsync(p => p.Number == number, ct);

        if (routing is null) return;
        if (isActive) routing.Activate();
        else routing.Deactivate();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
