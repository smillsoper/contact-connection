using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class FlowSessionRepository : IFlowSessionRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public FlowSessionRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<FlowSession?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.FlowSessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<FlowSession?> GetActiveByCallRecordAsync(Guid callRecordId, CancellationToken ct = default) =>
        Db.FlowSessions.FirstOrDefaultAsync(
            s => s.CallRecordId == callRecordId && s.Status == FlowSessionStatus.Active, ct);

    public async Task<IReadOnlyList<FlowSession>> GetByCallRecordAsync(Guid callRecordId, CancellationToken ct = default) =>
        await Db.FlowSessions
            .Where(s => s.CallRecordId == callRecordId)
            .OrderBy(s => s.StartedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FlowSession>> GetRecentAsync(Guid? flowId, int limit, CancellationToken ct = default) =>
        await Db.FlowSessions.AsNoTracking()
            .Where(s => flowId == null || s.FlowId == flowId)
            .OrderByDescending(s => s.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FlowSession>> GetActiveForAgentsAsync(
        IReadOnlyCollection<Guid> agentIds, DateTimeOffset updatedSince, CancellationToken ct = default)
    {
        if (agentIds.Count == 0) return [];
        var ids = agentIds.ToArray();
        return await Db.FlowSessions.AsNoTracking()
            .Where(s => ids.Contains(s.AgentId) && s.Status == FlowSessionStatus.Active && s.UpdatedAt >= updatedSince)
            .OrderBy(s => s.StartedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(FlowSession session, CancellationToken ct = default) =>
        await Db.FlowSessions.AddAsync(session, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
