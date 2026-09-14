namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Abstraction over SignalR push for tf_secure_collect capture progress — keeps Infrastructure
/// free of API dependencies. Implemented by SecureCollectNotifier in ContactConnection.Api, which
/// holds IHubContext&lt;FlowHub&gt;. Registered as scoped in Program.cs (after AddSignalR).
///
/// Only meaningful for a mid-bridge capture (an agent parked on hold who needs to know what's
/// happening) — a pre-agent capture has no agent connection to push to, so callers should skip
/// invoking this entirely rather than pass an empty agentId.
///
/// Neither method ever carries captured digits — fieldKey only identifies which field
/// (e.g. "card_number"), never its value.
/// </summary>
public interface ISecureCollectNotifier
{
    /// <summary>A capture field started or advanced on the caller's parked leg.</summary>
    Task NotifyProgressAsync(
        Guid agentId, Guid callRecordId, string fieldKey, int fieldIndex, int fieldCount,
        CancellationToken ct = default);

    /// <summary>The capture finished — outcome is "collected" | "failed" | "timeout" | "caller_hung_up".</summary>
    Task NotifyEndedAsync(Guid agentId, Guid callRecordId, string outcome, CancellationToken ct = default);
}
