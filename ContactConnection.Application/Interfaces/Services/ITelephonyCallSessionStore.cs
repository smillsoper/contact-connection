namespace ContactConnection.Application.Interfaces.Services;

public interface ITelephonyCallSessionStore
{
    Task SaveAsync(TelephonyCallSession session, CancellationToken ct = default);
    Task<TelephonyCallSession?> GetAsync(string channelUuid, CancellationToken ct = default);
    Task DeleteAsync(string channelUuid, CancellationToken ct = default);

    /// <summary>Return all active call sessions (used to find queued calls when an agent becomes available).</summary>
    Task<IReadOnlyList<TelephonyCallSession>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Store an arbitrary key/value pair with a TTL (e.g. whisper reverse mapping).</summary>
    Task SetKeyAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default);
    Task<string?> GetKeyAsync(string key, CancellationToken ct = default);
    Task DeleteKeyAsync(string key, CancellationToken ct = default);

    /// <summary>Atomic "set only if not already present" (Redis SETNX) — unlike SetKeyAsync, which
    /// always overwrites. Used for exclusive claims (e.g. RingStrategy.AutoAnswerBestAgent's
    /// per-agent claim key) where two callers racing on the same key must not both succeed —
    /// SetKeyAsync's plain unconditional write can't provide that guarantee on its own.
    /// Returns true if this call set the key (won the claim), false if it already existed.</summary>
    Task<bool> TrySetKeyAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default);
}
