using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IPortalApiEndpointRepository
{
    Task<List<PortalApiEndpoint>> GetByDefinitionAsync(Guid definitionId, CancellationToken ct = default);
    Task<PortalApiEndpoint?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(PortalApiEndpoint endpoint, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task DeleteAsync(PortalApiEndpoint endpoint, CancellationToken ct = default);
}
