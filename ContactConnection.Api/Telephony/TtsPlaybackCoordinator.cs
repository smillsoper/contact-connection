using System.Collections.Concurrent;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// In-process handshake between the streaming-TTS relay (TtsStreamRelayEndpoints — knows the
/// instant vendor synthesis is fully forwarded) and EslBackgroundService's per-channel play queue
/// (knows the instant local FreeSWITCH playback of every queued chunk has actually finished).
///
/// Why this exists: mod_audio_stream tears down its whole session — deleting every temp file it
/// wrote for it — the moment it processes a "uuid_audio_stream stop" command. A multi-chunk
/// utterance queues chunks faster than they play (synthesis is much faster than real-time
/// playback), so calling stop right after forwarding the last chunk (the old behavior) silently
/// deletes every chunk still sitting in the local play queue out from under uuid_broadcast —
/// live-verified as the root cause of "first chunk plays, then dead air, flow never resumes."
///
/// The fix: the relay must not call stop until local playback has actually caught up. Both sides
/// live in the same ASP.NET Core process (no cross-process IPC needed), so a simple per-channel
/// TaskCompletionSource is enough — SignalDrained/WaitForDrainAsync tolerate either call arriving
/// first (a signal that lands before anyone is waiting is still recorded).
/// </summary>
public sealed class TtsPlaybackCoordinator
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _drainSignals = new();

    /// <summary>
    /// Blocks until SignalDrained(channelUuid) is called, or timeout elapses — a safety net so a
    /// stuck/lost signal (a bug, or a chunk that genuinely never finishes) can't hang the call
    /// forever; the relay proceeds to stop regardless once the wait returns, and
    /// EslBackgroundService's own disconnect-triggered fallback forces the flow onward from there.
    /// </summary>
    public async Task WaitForDrainAsync(string channelUuid, TimeSpan timeout, CancellationToken ct = default)
    {
        var tcs = _drainSignals.GetOrAdd(
            channelUuid, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            await tcs.Task.WaitAsync(timeout, ct);
        }
        catch (TimeoutException) { /* safety net — proceed anyway */ }
        catch (OperationCanceledException) { /* caller cancelled — proceed anyway */ }
        finally
        {
            _drainSignals.TryRemove(channelUuid, out _);
        }
    }

    /// <summary>Wakes up (or pre-arms, if called first) the wait for this channel.</summary>
    public void SignalDrained(string channelUuid) =>
        _drainSignals
            .GetOrAdd(channelUuid, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult();
}
