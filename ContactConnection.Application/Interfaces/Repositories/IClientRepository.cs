using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IClientRepository
{
    Task<Client?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Client>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Client client, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
