using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ContactConnection.Infrastructure.Auth;

/// <summary>
/// Whether a support session (S184) is still live — checked on every request a support token makes, so ending a session
/// (or it expiring) stops its token at once. Cached for a few seconds so it isn't a database hit per request.
/// </summary>
public class SupportSessionReader(ContactConnectionDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);

    public static void Forget(IMemoryCache cache, Guid sessionId) => cache.Remove(Key(sessionId));

    private static string Key(Guid id) => $"support-session:{id}";

    /// <summary>The session's tenant and support account while it's live; null once ended or expired.</summary>
    public async Task<(Guid TenantId, Guid AgentId)?> GetLiveAsync(Guid sessionId, CancellationToken ct)
    {
        if (!cache.TryGetValue(Key(sessionId), out SupportSession? s))
        {
            s = await db.SupportSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId, ct);
            cache.Set(Key(sessionId), s, CacheFor);
        }
        return s is not null && s.IsActive(DateTimeOffset.UtcNow) ? (s.TenantId, s.AgentId) : null;
    }
}
