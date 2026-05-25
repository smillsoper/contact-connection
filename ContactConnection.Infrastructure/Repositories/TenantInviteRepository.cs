using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class TenantInviteRepository : ITenantInviteRepository
{
    private readonly ContactConnectionDbContext _db;

    public TenantInviteRepository(ContactConnectionDbContext db) => _db = db;

    public async Task<TenantInvite?> GetByTokenAsync(string token, CancellationToken ct = default)
        => await _db.TenantInvites
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Token == token, ct);

    public async Task AddAsync(TenantInvite invite, CancellationToken ct = default)
        => await _db.TenantInvites.AddAsync(invite, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}
