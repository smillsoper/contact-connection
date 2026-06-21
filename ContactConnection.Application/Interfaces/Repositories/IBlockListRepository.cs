using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IBlockListRepository
{
    Task<bool> IsBlockedAsync(Guid tenantId, string phoneNumber, CancellationToken ct = default);
    Task<IReadOnlyList<BlockListEntry>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<BlockListEntry?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task AddAsync(BlockListEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
