using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Tenants;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class TenantApiPreferenceRepository : ITenantApiPreferenceRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public TenantApiPreferenceRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<List<TenantApiPreference>> GetAllAsync(CancellationToken ct = default) =>
        Db.TenantApiPreferences.OrderBy(p => p.ApiSubType).ToListAsync(ct);

    public Task<TenantApiPreference?> GetBySubTypeAsync(string apiSubType, CancellationToken ct = default) =>
        Db.TenantApiPreferences.FirstOrDefaultAsync(p => p.ApiSubType == apiSubType, ct);

    public async Task UpsertAsync(string apiSubType, Guid portalApiEndpointId, CancellationToken ct = default)
    {
        var existing = await GetBySubTypeAsync(apiSubType, ct);
        if (existing is not null)
        {
            existing.SetEndpoint(portalApiEndpointId);
        }
        else
        {
            var pref = TenantApiPreference.Create(apiSubType, portalApiEndpointId);
            await Db.TenantApiPreferences.AddAsync(pref, ct);
        }
    }

    public async Task DeleteBySubTypeAsync(string apiSubType, CancellationToken ct = default)
    {
        var existing = await GetBySubTypeAsync(apiSubType, ct);
        if (existing is not null)
            Db.TenantApiPreferences.Remove(existing);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
