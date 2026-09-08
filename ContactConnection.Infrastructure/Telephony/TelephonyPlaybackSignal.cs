using System.Collections.Concurrent;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// Default <see cref="ITelephonyPlaybackSignal"/> — a per-channel <see cref="TaskCompletionSource"/>
/// registry. Single API instance owns the ESL connection, so an in-process rendezvous is enough;
/// nothing crosses instances.
/// </summary>
public sealed class TelephonyPlaybackSignal : ITelephonyPlaybackSignal
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _waiters = new();

    // Signals that arrived with no waiter yet, with the instant they landed. WaitAsync consumes a
    // latch newer than a few seconds so a playback that finishes between the transfer command and
    // the await isn't lost; older entries are swept on access.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _latched = new();
    private static readonly TimeSpan LatchTtl = TimeSpan.FromSeconds(10);

    public async Task<bool> WaitAsync(string channelUuid, TimeSpan timeout, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(channelUuid)) return false;

        if (_latched.TryRemove(channelUuid, out var at) && DateTimeOffset.UtcNow - at <= LatchTtl)
            return true;

        var tcs = _waiters.GetOrAdd(
            channelUuid,
            _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        await using var reg = timeoutCts.Token.Register(
            static s => ((TaskCompletionSource<bool>)s!).TrySetResult(false), tcs);
        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _waiters.TryRemove(new KeyValuePair<string, TaskCompletionSource<bool>>(channelUuid, tcs));
        }
    }

    public void Signal(string channelUuid)
    {
        if (string.IsNullOrEmpty(channelUuid)) return;

        if (_waiters.TryGetValue(channelUuid, out var tcs) && tcs.TrySetResult(true))
            return;

        // No one waiting yet — latch it for a WaitAsync that's about to run.
        _latched[channelUuid] = DateTimeOffset.UtcNow;
    }
}
