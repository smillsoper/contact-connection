using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class WebhookEndpointRepository : IWebhookEndpointRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public WebhookEndpointRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<List<WebhookEndpoint>> GetAllAsync(CancellationToken ct = default) =>
        Db.WebhookEndpoints.OrderByDescending(w => w.CreatedAt).ToListAsync(ct);

    public Task<WebhookEndpoint?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<WebhookEndpoint?> GetByTokenAsync(string token, CancellationToken ct = default) =>
        Db.WebhookEndpoints.FirstOrDefaultAsync(w => w.Token == token, ct);

    public async Task AddAsync(WebhookEndpoint endpoint, CancellationToken ct = default) =>
        await Db.WebhookEndpoints.AddAsync(endpoint, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);

    public Task DeleteAsync(WebhookEndpoint endpoint, CancellationToken ct = default)
    {
        Db.WebhookEndpoints.Remove(endpoint);
        return Task.CompletedTask;
    }
}
