using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Tenants;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class TenantApiDefinitionRepository : ITenantApiDefinitionRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public TenantApiDefinitionRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<List<TenantApiDefinition>> GetAllAsync(CancellationToken ct = default) =>
        Db.TenantApiDefinitions.OrderBy(d => d.ApiType).ThenBy(d => d.Name).ToListAsync(ct);

    public Task<List<TenantApiDefinition>> GetByTypeAsync(string apiType, CancellationToken ct = default) =>
        Db.TenantApiDefinitions.Where(d => d.ApiType == apiType).OrderBy(d => d.Name).ToListAsync(ct);

    public Task<TenantApiDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.TenantApiDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task AddAsync(TenantApiDefinition definition, CancellationToken ct = default) =>
        await Db.TenantApiDefinitions.AddAsync(definition, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);

    public Task DeleteAsync(TenantApiDefinition definition, CancellationToken ct = default)
    {
        Db.TenantApiDefinitions.Remove(definition);
        return Task.CompletedTask;
    }
}
