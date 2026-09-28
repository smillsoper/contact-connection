using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class NumberProviderRepository(ScopedTenantDbContextFactory factory) : INumberProviderRepository
{
    private TenantDbContext? _ctx;
    private TenantDbContext Ctx => _ctx ??= factory.Create();

    public Task<List<NumberProvider>> GetAllAsync(CancellationToken ct = default)
        => Ctx.NumberProviders.OrderBy(p => p.Name).ToListAsync(ct);

    public Task<NumberProvider?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Ctx.NumberProviders.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<NumberProvider?> GetActiveByApiKeyHashAsync(string apiKeyHash, CancellationToken ct = default)
        => Ctx.NumberProviders.FirstOrDefaultAsync(p => p.ApiKeyHash == apiKeyHash && p.IsActive, ct);

    public Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct = default)
        => Ctx.NumberProviders.AnyAsync(p => p.Name.ToLower() == name.Trim().ToLower() && p.Id != exceptId, ct);

    public async Task AddAsync(NumberProvider provider, CancellationToken ct = default)
        => await Ctx.NumberProviders.AddAsync(provider, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => Ctx.SaveChangesAsync(ct);
}
