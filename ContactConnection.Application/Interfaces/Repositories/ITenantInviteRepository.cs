using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ITenantInviteRepository
{
    Task<TenantInvite?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task AddAsync(TenantInvite invite, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
