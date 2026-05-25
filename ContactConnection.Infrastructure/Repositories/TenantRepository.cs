using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class TenantRepository : ITenantRepository
{
    private readonly ContactConnectionDbContext _db;

    public TenantRepository(ContactConnectionDbContext db) => _db = db;

    public Task<List<Tenant>> GetAllAsync(CancellationToken ct = default) =>
        _db.Tenants.OrderBy(t => t.Name).ToListAsync(ct);

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken ct = default) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == subdomain.ToLowerInvariant(), ct);

    public Task<Tenant?> GetByCustomDomainAsync(string customDomain, CancellationToken ct = default) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.CustomDomain == customDomain.ToLowerInvariant(), ct);

    public Task<bool> SubdomainExistsAsync(string subdomain, CancellationToken ct = default) =>
        _db.Tenants.AnyAsync(t => t.Subdomain == subdomain.ToLowerInvariant(), ct);

    public async Task AddAsync(Tenant tenant, CancellationToken ct = default) =>
        await _db.Tenants.AddAsync(tenant, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
