using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class CustomUnavailableCodeRepository : ICustomUnavailableCodeRepository
{
    private TenantDbContext? _db;
    private readonly ScopedTenantDbContextFactory _factory;

    public CustomUnavailableCodeRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    private TenantDbContext Db => _db ??= _factory.Create();

    public Task<List<CustomUnavailableCode>> GetAllAsync(CancellationToken ct = default) =>
        Db.CustomUnavailableCodes.OrderBy(c => c.Name).ToListAsync(ct);

    public Task<List<CustomUnavailableCode>> GetForRoleAsync(string roleId, CancellationToken ct = default) =>
        Db.CustomUnavailableCodes
            .Where(c => c.IsActive && (c.Roles.Length == 0 || c.Roles.Contains(roleId)))
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public Task<CustomUnavailableCode?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.CustomUnavailableCodes.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(CustomUnavailableCode code, CancellationToken ct = default) =>
        await Db.CustomUnavailableCodes.AddAsync(code, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
