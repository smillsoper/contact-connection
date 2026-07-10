using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ICustomUnavailableCodeRepository
{
    Task<List<CustomUnavailableCode>> GetAllAsync(CancellationToken ct = default);
    Task<List<CustomUnavailableCode>> GetForRoleAsync(string role, CancellationToken ct = default);
    Task<CustomUnavailableCode?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(CustomUnavailableCode code, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
