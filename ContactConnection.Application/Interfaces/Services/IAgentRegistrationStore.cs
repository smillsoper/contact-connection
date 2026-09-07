namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Tracks which agent SIP extensions are currently REGISTERed with FreeSWITCH — a signal
/// distinct from the agent's own status (available / on-call / …). An agent can be "Available"
/// in <see cref="IAgentStateStore"/> while their softphone is not actually registered (e.g. they
/// navigated away from the portal page and the registration is just aging out toward its TTL);
/// a call routed there would ring into a dead transport. Supervisors need to see the difference.
///
/// Runtime-only, in-memory: FreeSWITCH's own registration table is the source of truth.
/// <see cref="EslBackgroundService"/> seeds this from <c>show registrations</c> on every ESL
/// (re)connect and keeps it live from sofia::register / ::unregister / ::expire events.
/// </summary>
public interface IAgentRegistrationStore
{
    /// <summary>Mark an extension registered. If already present, the original <paramref name="since"/> is kept.</summary>
    void Set(Guid tenantId, string sipExtension, DateTimeOffset since);

    /// <summary>Mark an extension no longer registered.</summary>
    void Remove(Guid tenantId, string sipExtension);

    /// <summary>Resync the whole store to a fresh snapshot (used on ESL reconnect).</summary>
    void ReplaceAll(IEnumerable<(Guid TenantId, string SipExtension, DateTimeOffset Since)> snapshot);

    /// <summary>Current registration for an extension, or null when not registered.</summary>
    AgentRegistrationInfo? Get(Guid tenantId, string sipExtension);
}

/// <param name="Since">When the extension was first observed registered in this process's lifetime.</param>
public sealed record AgentRegistrationInfo(DateTimeOffset Since);
