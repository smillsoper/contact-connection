using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// The caller's hold-loop state (tf_play _play_*, streaming TTS _tts_*, tf_delay _delay_*, transfer announcements
/// _announce_*) — cleared BEFORE a caller is connected to an agent (S185). Stopping the caller's hold audio for the bridge
/// fires PLAYBACK_STOP; while this state is still saved, EslBackgroundService reads that stop as "the loop finished" and
/// starts the next hold / ring-tone pass — the caller kept hearing queue audio for seconds after the agent's connect
/// tone. With the state gone first, the stop is ignored. (EslBackgroundService.ClearHoldLoopVars clears the same keys
/// once the bridge is reported — too late for this race.)
/// </summary>
public static class HoldLoopState
{
    private static readonly string[] Prefixes = ["_play_", "_tts_", "_delay_", "_announce_"];

    public static bool IsHoldLoopKey(string key) => Prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));

    /// <summary>Removes the hold-loop keys from a loaded session (caller saves it).</summary>
    public static void Remove(TelephonyCallSession session)
    {
        foreach (var k in session.Vars.Keys.Where(IsHoldLoopKey).ToList()) session.Vars.Remove(k);
    }

    /// <summary>Clears them from the saved session now (and from the node's context, so the engine's later sync
    /// doesn't write them back).</summary>
    public static async Task ClearAsync(ITelephonyCallSessionStore store, string channelUuid, TelephonyFlowContext? ctx, CancellationToken ct)
    {
        if (ctx is not null)
            foreach (var k in ctx.Vars.Keys.Where(IsHoldLoopKey).ToList()) ctx.RemoveSessionVar(k);
        var session = await store.GetAsync(channelUuid, ct);
        if (session is null || !session.Vars.Keys.Any(IsHoldLoopKey)) return;
        Remove(session);
        await store.SaveAsync(session, ct);
    }
}
