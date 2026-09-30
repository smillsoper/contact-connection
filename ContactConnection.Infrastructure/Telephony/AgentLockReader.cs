using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>See IAgentLockReader. Cached briefly per agent (hit on every request), invalidated on
/// lock/unlock by the instance that made the change.</summary>
public sealed class AgentLockReader(ITenantDbContextFactory dbFactory, IMemoryCache cache) : IAgentLockReader
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private sealed record Entry(AgentLockInfo? Lock);

    private static string Key(Guid agentId) => $"agent-lock:{agentId}";

    public async Task<AgentLockInfo?> GetAsync(string tenantSchemaName, Guid agentId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(agentId), out Entry? hit) && hit is not null) return hit.Lock;

        await using var db = dbFactory.Create(tenantSchemaName);
        var row = await db.Agents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => new { a.StatusLockedAt, a.SignInLocked, a.StatusLockReason, a.StatusLockedByName })
            .FirstOrDefaultAsync(ct);
        var info = row?.StatusLockedAt is { } at
            ? new AgentLockInfo(row.SignInLocked, row.StatusLockReason, row.StatusLockedByName, at)
            : null;
        cache.Set(Key(agentId), new Entry(info), Ttl);
        return info;
    }

    public void Invalidate(Guid agentId) => cache.Remove(Key(agentId));
}
