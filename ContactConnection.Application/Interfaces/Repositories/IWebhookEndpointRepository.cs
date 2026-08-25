using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IWebhookEndpointRepository
{
    Task<WebhookEndpoint?> GetByTenantApiEndpointIdAsync(Guid tenantApiEndpointId, CancellationToken ct = default);
    Task<WebhookEndpoint?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WebhookEndpoint?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task AddAsync(WebhookEndpoint endpoint, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task DeleteAsync(WebhookEndpoint endpoint, CancellationToken ct = default);
}
