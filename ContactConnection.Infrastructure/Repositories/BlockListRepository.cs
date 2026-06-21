using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class BlockListRepository : IBlockListRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public BlockListRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public async Task<bool> IsBlockedAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = await Db.BlockListEntries
            .Where(e => e.TenantId == tenantId && (e.ExpiresAt == null || e.ExpiresAt > now))
            .Select(e => new { e.PhoneNumber, e.MatchType })
            .ToListAsync(ct);

        return entries.Any(e =>
            e.MatchType == BlockListMatchType.Exact
                ? e.PhoneNumber == phoneNumber
                : phoneNumber.StartsWith(e.PhoneNumber, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<BlockListEntry>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await Db.BlockListEntries
            .Where(e => e.TenantId == tenantId && (e.ExpiresAt == null || e.ExpiresAt > now))
            .OrderBy(e => e.PhoneNumber)
            .ToListAsync(ct);
    }

    public Task<BlockListEntry?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Db.BlockListEntries.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId, ct);

    public async Task AddAsync(BlockListEntry entry, CancellationToken ct = default) =>
        await Db.BlockListEntries.AddAsync(entry, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
