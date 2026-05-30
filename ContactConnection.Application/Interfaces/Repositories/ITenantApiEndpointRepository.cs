using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ITenantApiEndpointRepository
{
    Task<List<TenantApiEndpoint>> GetByDefinitionAsync(Guid definitionId, CancellationToken ct = default);
    Task<TenantApiEndpoint?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(TenantApiEndpoint endpoint, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task DeleteAsync(TenantApiEndpoint endpoint, CancellationToken ct = default);
}
