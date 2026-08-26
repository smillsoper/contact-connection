using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class WebhookEventRepository : IWebhookEventRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public WebhookEventRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<bool> ExistsAsync(Guid webhookEndpointId, string bodyHash, CancellationToken ct = default) =>
        Db.WebhookEvents.AnyAsync(e => e.WebhookEndpointId == webhookEndpointId && e.BodyHash == bodyHash, ct);

    public Task<List<WebhookEvent>> ListByEndpointAsync(Guid webhookEndpointId, int take = 50, CancellationToken ct = default) =>
        Db.WebhookEvents
            .Where(e => e.WebhookEndpointId == webhookEndpointId)
            .OrderByDescending(e => e.ReceivedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task AddAsync(WebhookEvent webhookEvent, CancellationToken ct = default) =>
        await Db.WebhookEvents.AddAsync(webhookEvent, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
