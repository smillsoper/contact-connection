using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class StoredValueRepository : IStoredValueRepository
{
    private TenantDbContext? _ctx;
    private readonly ScopedTenantDbContextFactory _factory;

    public StoredValueRepository(ScopedTenantDbContextFactory factory)
        => _factory = factory;

    private TenantDbContext Ctx => _ctx ??= _factory.Create();

    public Task<StoredValue?> GetAsync(Guid tenantId, string scope, Guid scopeId, string keyName, CancellationToken ct = default)
        => Ctx.StoredValues.FirstOrDefaultAsync(v =>
            v.TenantId == tenantId && v.Scope == scope && v.ScopeId == scopeId && v.KeyName == keyName, ct);

    public async Task AddAsync(StoredValue value, CancellationToken ct = default)
        => await Ctx.StoredValues.AddAsync(value, ct);

    public async Task<IReadOnlyList<Guid>> FindExpiredOldestFirstAsync(int limit, CancellationToken ct = default) =>
        await Ctx.StoredValues
            .Where(v => v.ExpiresAt != null && v.ExpiresAt <= DateTimeOffset.UtcNow)
            .OrderBy(v => v.ExpiresAt)
            .Select(v => v.Id)
            .Take(limit)
            .ToListAsync(ct);

    public Task<StoredValue?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Ctx.StoredValues.FirstOrDefaultAsync(v => v.Id == id, ct);

    public void Remove(StoredValue value) => Ctx.StoredValues.Remove(value);

    public Task SaveChangesAsync(CancellationToken ct = default) => Ctx.SaveChangesAsync(ct);
}
