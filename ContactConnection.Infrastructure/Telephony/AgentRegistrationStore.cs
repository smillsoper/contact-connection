using System.Collections.Concurrent;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// In-memory <see cref="IAgentRegistrationStore"/> — a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// keyed by (tenantId, extension). Not Redis-backed on purpose: SIP registrations are ephemeral
/// runtime state and FreeSWITCH's own table is authoritative, so on an API restart
/// <see cref="EslBackgroundService"/> re-seeds from <c>show registrations</c> rather than trusting
/// a stale cache. Singleton.
/// </summary>
public sealed class AgentRegistrationStore : IAgentRegistrationStore
{
    private readonly ConcurrentDictionary<(Guid TenantId, string Ext), DateTimeOffset> _reg = new();

    private static (Guid, string) K(Guid tenantId, string ext) => (tenantId, ext.Trim());

    public void Set(Guid tenantId, string sipExtension, DateTimeOffset since)
    {
        if (string.IsNullOrWhiteSpace(sipExtension)) return;
        // Keep the earliest observed time — a re-REGISTER every ~expiry interval must not
        // reset the "registered since" clock the supervisor UI shows.
        _reg.AddOrUpdate(K(tenantId, sipExtension), since, (_, existing) => existing < since ? existing : since);
    }

    public void Remove(Guid tenantId, string sipExtension)
    {
        if (string.IsNullOrWhiteSpace(sipExtension)) return;
        _reg.TryRemove(K(tenantId, sipExtension), out _);
    }

    public void ReplaceAll(IEnumerable<(Guid TenantId, string SipExtension, DateTimeOffset Since)> snapshot)
    {
        var next = new HashSet<(Guid, string)>();
        foreach (var (tenantId, ext, since) in snapshot)
        {
            if (string.IsNullOrWhiteSpace(ext)) continue;
            var key = K(tenantId, ext);
            next.Add(key);
            _reg.AddOrUpdate(key, since, (_, existing) => existing < since ? existing : since);
        }
        foreach (var key in _reg.Keys)
            if (!next.Contains(key)) _reg.TryRemove(key, out _);
    }

    public AgentRegistrationInfo? Get(Guid tenantId, string sipExtension)
    {
        if (string.IsNullOrWhiteSpace(sipExtension)) return null;
        return _reg.TryGetValue(K(tenantId, sipExtension), out var since)
            ? new AgentRegistrationInfo(since)
            : null;
    }
}
