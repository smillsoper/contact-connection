using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Role>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Role role, CancellationToken ct = default);
    void Delete(Role role);
    Task SaveChangesAsync(CancellationToken ct = default);
}
