using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ITenantAdminInviteRepository
{
    Task<TenantAdminInvite?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task AddAsync(TenantAdminInvite invite, CancellationToken ct = default);
    Task DeleteByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
