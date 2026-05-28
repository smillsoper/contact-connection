using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IPortalApiDefinitionRepository
{
    Task<List<PortalApiDefinition>> GetAllAsync(CancellationToken ct = default);
    Task<List<PortalApiDefinition>> GetByTypeAsync(string apiType, CancellationToken ct = default);
    Task<PortalApiDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(PortalApiDefinition definition, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task DeleteAsync(PortalApiDefinition definition, CancellationToken ct = default);
}
