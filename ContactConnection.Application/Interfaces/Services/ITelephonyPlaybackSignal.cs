namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// In-process rendezvous between a telephony node handler that needs to block until a
/// foreground <c>tts_play</c> playback finishes and the ESL event loop that observes the
/// <c>contactconnection::tts_done</c> CUSTOM event for that channel.
///
/// Only <see cref="TransferNodeHandler"/> currently awaits — its pre-handoff announcement must
/// finish before the caller is enqueued / the flow is switched. tf_play and tf_whisper don't
/// wait here: they return terminally and are resumed from the same event by
/// <c>EslBackgroundService.HandleTtsDoneAsync</c>.
///
/// Singleton. Keyed by FreeSWITCH channel UUID. A signal that arrives before anyone is waiting
/// is latched briefly so a fast playback can't be missed.
/// </summary>
public interface ITelephonyPlaybackSignal
{
    /// <summary>
    /// Waits for <see cref="Signal"/> on <paramref name="channelUuid"/>. Returns <c>true</c> if the
    /// signal arrived (including a latched one from just before this call), <c>false</c> on timeout
    /// or cancellation — callers proceed regardless, a timeout just means the announcement may still
    /// be playing.
    /// </summary>
    Task<bool> WaitAsync(string channelUuid, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>Releases a pending <see cref="WaitAsync"/> for this channel, or latches for the next one.</summary>
    void Signal(string channelUuid);
}
