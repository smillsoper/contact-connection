using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public RoleRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<Role>> GetAllAsync(CancellationToken ct = default) =>
        Db.Roles.OrderBy(r => r.Name).ToListAsync(ct);

    public async Task AddAsync(Role role, CancellationToken ct = default) =>
        await Db.Roles.AddAsync(role, ct);

    public void Delete(Role role) =>
        Db.Roles.Remove(role);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
