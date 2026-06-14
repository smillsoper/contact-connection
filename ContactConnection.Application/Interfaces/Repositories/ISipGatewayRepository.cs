using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ISipGatewayRepository
{
    Task<SipGateway?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SipGateway?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<List<SipGateway>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(SipGateway gateway, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
