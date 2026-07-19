using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class DashboardRepository : IDashboardRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public DashboardRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.Dashboards.FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<List<Dashboard>> GetVisibleAsync(Guid tenantId, Guid agentId, CancellationToken ct = default) =>
        Db.Dashboards
            .Where(d => d.TenantId == tenantId && (d.IsShared || d.CreatedByAgentId == agentId))
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Dashboard dashboard, CancellationToken ct = default) =>
        await Db.Dashboards.AddAsync(dashboard, ct);

    public void Delete(Dashboard dashboard) => Db.Dashboards.Remove(dashboard);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
