using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class ClientRepository : IClientRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public ClientRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<Client?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.Clients.Include(c => c.Campaigns).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<Client>> GetAllAsync(CancellationToken ct = default) =>
        Db.Clients.OrderBy(c => c.Name).ToListAsync(ct);

    public async Task AddAsync(Client client, CancellationToken ct = default) =>
        await Db.Clients.AddAsync(client, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
