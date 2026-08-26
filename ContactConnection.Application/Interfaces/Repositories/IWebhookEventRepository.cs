using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IWebhookEventRepository
{
    /// <summary>Dedup check — has an event with this exact body already been received on this
    /// webhook endpoint? Explicit application-level check (not just a DB unique-constraint
    /// catch) so it's testable against EF InMemory too; a real unique index on
    /// (WebhookEndpointId, BodyHash) backs it as defense-in-depth against races.</summary>
    Task<bool> ExistsAsync(Guid webhookEndpointId, string bodyHash, CancellationToken ct = default);

    Task<List<WebhookEvent>> ListByEndpointAsync(Guid webhookEndpointId, int take = 50, CancellationToken ct = default);
    Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
