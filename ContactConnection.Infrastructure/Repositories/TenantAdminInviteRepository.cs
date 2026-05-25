using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class TenantAdminInviteRepository : ITenantAdminInviteRepository
{
    private readonly ContactConnectionDbContext _db;

    public TenantAdminInviteRepository(ContactConnectionDbContext db) => _db = db;

    public async Task<TenantAdminInvite?> GetByTokenAsync(string token, CancellationToken ct = default)
        => await _db.TenantAdminInvites
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Token == token, ct);

    public async Task AddAsync(TenantAdminInvite invite, CancellationToken ct = default)
        => await _db.TenantAdminInvites.AddAsync(invite, ct);

    public Task DeleteByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.TenantAdminInvites.Where(i => i.TenantId == tenantId).ExecuteDeleteAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}
