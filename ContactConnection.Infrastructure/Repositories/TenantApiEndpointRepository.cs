using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Tenants;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class TenantApiEndpointRepository : ITenantApiEndpointRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public TenantApiEndpointRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<List<TenantApiEndpoint>> GetByDefinitionAsync(Guid definitionId, CancellationToken ct = default) =>
        Db.TenantApiEndpoints
            .Where(e => e.DefinitionId == definitionId)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.Name)
            .ToListAsync(ct);

    public Task<TenantApiEndpoint?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.TenantApiEndpoints.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task AddAsync(TenantApiEndpoint endpoint, CancellationToken ct = default) =>
        await Db.TenantApiEndpoints.AddAsync(endpoint, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);

    public Task DeleteAsync(TenantApiEndpoint endpoint, CancellationToken ct = default)
    {
        Db.TenantApiEndpoints.Remove(endpoint);
        return Task.CompletedTask;
    }
}
