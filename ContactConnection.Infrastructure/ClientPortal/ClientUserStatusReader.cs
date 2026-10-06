using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ContactConnection.Infrastructure.ClientPortal;

/// <summary>
/// Checked on every client-portal request (S181) so a deactivated or deleted client user's tokens stop working at once
/// rather than living out their lifetime. Cached briefly; the admin endpoints invalidate on change.
/// </summary>
public sealed class ClientUserStatusReader(ITenantDbContextFactory dbFactory, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static string Key(Guid id) => $"client-user-active:{id}";

    public async Task<bool> IsActiveAsync(string tenantSchemaName, Guid clientUserId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(clientUserId), out bool hit)) return hit;
        await using var db = dbFactory.Create(tenantSchemaName);
        var active = await db.ClientUsers.AsNoTracking()
            .AnyAsync(u => u.Id == clientUserId && u.IsActive && u.PasswordHash != null, ct);
        cache.Set(Key(clientUserId), active, Ttl);
        return active;
    }

    public void Invalidate(Guid clientUserId) => cache.Remove(Key(clientUserId));
}
