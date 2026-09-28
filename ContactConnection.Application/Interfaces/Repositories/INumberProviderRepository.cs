using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface INumberProviderRepository
{
    Task<List<NumberProvider>> GetAllAsync(CancellationToken ct = default);
    Task<NumberProvider?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>The active provider holding this API key (by SHA-256 hash) — external-routing auth.</summary>
    Task<NumberProvider?> GetActiveByApiKeyHashAsync(string apiKeyHash, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct = default);
    Task AddAsync(NumberProvider provider, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
