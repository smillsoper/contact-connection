using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ITenantApiDefinitionRepository
{
    Task<List<TenantApiDefinition>> GetAllAsync(CancellationToken ct = default);
    Task<List<TenantApiDefinition>> GetByTypeAsync(string apiType, CancellationToken ct = default);
    Task<TenantApiDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(TenantApiDefinition definition, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task DeleteAsync(TenantApiDefinition definition, CancellationToken ct = default);
}
