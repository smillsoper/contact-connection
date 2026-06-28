using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class AgentRepository : IAgentRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public AgentRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<List<Agent>> GetAllAsync(CancellationToken ct = default) =>
        Db.Agents.Include(a => a.CustomRole).OrderBy(a => a.LastName).ThenBy(a => a.FirstName).ToListAsync(ct);

    public Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.Agents.Include(a => a.CustomRole).FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Agent?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Db.Agents.FirstOrDefaultAsync(
            a => a.Email == email.ToLowerInvariant() && a.IsActive, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        Db.Agents.AnyAsync(a => a.Email == email.ToLowerInvariant(), ct);

    public async Task AddAsync(Agent agent, CancellationToken ct = default) =>
        await Db.Agents.AddAsync(agent, ct);

    public Task DeleteAllAsync(CancellationToken ct = default) =>
        Db.Agents.ExecuteDeleteAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);

    public async Task<int> GetMaxSipExtensionAsync(CancellationToken ct = default)
    {
        var extensions = await Db.Agents
            .Where(a => a.SipExtension != null)
            .Select(a => a.SipExtension!)
            .ToListAsync(ct);

        return extensions.Count == 0 ? 999 : extensions.Select(int.Parse).Max();
    }

    public Task<Agent?> GetBySipExtensionAsync(string extension, CancellationToken ct = default) =>
        Db.Agents.FirstOrDefaultAsync(a => a.SipExtension == extension && a.IsActive, ct);
}
