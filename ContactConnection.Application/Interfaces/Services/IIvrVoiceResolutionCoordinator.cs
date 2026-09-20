namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Arbitrates the race between the two independent ways a voice-enabled tf_ivr_menu node
/// (S148) can resolve — a DTMF keypress (EslBackgroundService.HandleDtmfAsync) or a matched
/// spoken phrase (SttStreamRelayEndpoints) — since both run concurrently once the node starts
/// listening and either can win. Exactly one of them should ever actually stop the capture and
/// resume the flow for a given call; the other must discover it lost and do nothing further.
///
/// The claim itself is a Redis SETNX (ITelephonyCallSessionStore.TrySetKeyAsync) rather than
/// in-process state — same "exclusive claim" primitive already used for
/// RingStrategy.AutoAnswerBestAgent's per-agent claim — so it's correct even though the two
/// racing paths (a background-service event handler, an ASP.NET WebSocket endpoint) are
/// different code paths within the same process.
/// </summary>
public interface IIvrVoiceResolutionCoordinator
{
    /// <summary>
    /// Attempts to claim resolution of <paramref name="channelUuid"/>'s voice-enabled IVR menu.
    /// Returns false immediately (no side effects) if another caller already claimed it. On a
    /// successful claim: clears the node's `_ivr_voice_*` session vars, stops the audio-stream
    /// capture (uuid_audio_stream stop), records a call-trace step for the menu node carrying
    /// <paramref name="resolutionDetail"/> (e.g. the transcript heard, or which digit was
    /// pressed — surfaced in the trace UI so tenants can see exactly what STT transcribed
    /// without digging through logs), and — when <paramref name="target"/> is non-null —
    /// resumes the telephony flow at that node. Returns true.
    /// </summary>
    Task<bool> TryResolveAsync(
        string channelUuid, string? target, IEslCommander esl, string? resolutionDetail = null,
        CancellationToken ct = default);
}
