using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IDashboardRepository
{
    Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Dashboards visible to an agent: their own plus any shared in the tenant.</summary>
    Task<List<Dashboard>> GetVisibleAsync(Guid tenantId, Guid agentId, CancellationToken ct = default);

    Task AddAsync(Dashboard dashboard, CancellationToken ct = default);
    void Delete(Dashboard dashboard);
    Task SaveChangesAsync(CancellationToken ct = default);
}
