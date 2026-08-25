using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IWebhookEndpointRepository
{
    /// <summary>Every webhook configured across the tenant, regardless of which API Definition/
    /// Endpoint it belongs to — backs the tenant-wide Webhooks dashboard page (as opposed to the
    /// per-endpoint config panel, which only ever needs one at a time).</summary>
    Task<List<WebhookEndpoint>> GetAllAsync(CancellationToken ct = default);

    Task<WebhookEndpoint?> GetByTenantApiEndpointIdAsync(Guid tenantApiEndpointId, CancellationToken ct = default);
    Task<WebhookEndpoint?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WebhookEndpoint?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task AddAsync(WebhookEndpoint endpoint, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task DeleteAsync(WebhookEndpoint endpoint, CancellationToken ct = default);
}
