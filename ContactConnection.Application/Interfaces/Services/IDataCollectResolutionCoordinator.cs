namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Arbitrates the race between the two independent ways a voice-enabled tf_data_collect node can
/// resolve — a DTMF sequence completing via play_and_get_digits (EslBackgroundService's
/// contactconnection::data_collect_done handler) or a free-form spoken value transcribed by
/// SttStreamRelayEndpoints — since both run concurrently once the node starts collecting and
/// either can win. Structurally the same shape as IIvrVoiceResolutionCoordinator (same Redis
/// SETNX claim primitive), but kept as its own type since the resolution semantics differ: this
/// one writes a captured value into a named flow variable and picks between exactly two node
/// exits ("collected" / "timeout") instead of resolving to an arbitrary target directly.
/// </summary>
public interface IDataCollectResolutionCoordinator
{
    /// <summary>
    /// Attempts to claim resolution of <paramref name="channelUuid"/>'s data-collect node.
    /// Returns false immediately (no side effects) if another caller already claimed it. On a
    /// successful claim: stops any in-flight voice capture (uuid_audio_stream stop, best-effort),
    /// writes <paramref name="capturedValue"/> into the node's configured flow variable and
    /// resumes at its "collected" transition when non-null/non-empty, or resumes at its
    /// "timeout" transition otherwise. Records a call-trace step carrying
    /// <paramref name="resolutionDetail"/> (what was actually captured — a DTMF string or a
    /// transcript — so tenants can see it without digging through logs). Returns true.
    /// </summary>
    Task<bool> TryResolveAsync(
        string channelUuid, string? capturedValue, IEslCommander esl, string? resolutionDetail = null,
        CancellationToken ct = default);
}
