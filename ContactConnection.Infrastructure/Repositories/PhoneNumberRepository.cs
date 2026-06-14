using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class PhoneNumberRepository : IPhoneNumberRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public PhoneNumberRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<PhoneNumber?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.PhoneNumbers.Include(p => p.Campaign).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<PhoneNumber?> GetByNumberAsync(string number, CancellationToken ct = default) =>
        Db.PhoneNumbers
            .Include(p => p.Campaign)
            .FirstOrDefaultAsync(p => p.Number == number && p.IsActive, ct);

    public Task<List<PhoneNumber>> GetByCampaignIdAsync(Guid campaignId, CancellationToken ct = default) =>
        Db.PhoneNumbers.Where(p => p.CampaignId == campaignId).OrderBy(p => p.Number).ToListAsync(ct);

    public async Task AddAsync(PhoneNumber phoneNumber, CancellationToken ct = default) =>
        await Db.PhoneNumbers.AddAsync(phoneNumber, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
