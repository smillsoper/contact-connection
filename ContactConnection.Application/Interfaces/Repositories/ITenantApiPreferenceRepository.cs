using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ITenantApiPreferenceRepository
{
    Task<List<TenantApiPreference>> GetAllAsync(CancellationToken ct = default);
    Task<TenantApiPreference?> GetBySubTypeAsync(string apiSubType, CancellationToken ct = default);
    Task UpsertAsync(string apiSubType, Guid portalApiEndpointId, string? settingsJson = null, CancellationToken ct = default);
    Task DeleteBySubTypeAsync(string apiSubType, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
