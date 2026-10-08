using System.Collections.Concurrent;

namespace ContactConnection.Worker;

/// <summary>
/// Each background job reports in once per loop (S184 platform health) — the health service flags a job that has
/// stopped running on schedule.
/// </summary>
public static class JobHeartbeats
{
    public sealed record Beat(DateTimeOffset At, TimeSpan Interval);

    private static readonly ConcurrentDictionary<string, Beat> Beats = new();

    /// <summary>When the Worker started — a job that hasn't reported yet is only late once it's had time to.</summary>
    public static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    /// <summary>The jobs and how often each runs — known up front so one that never reports still shows.</summary>
    private static readonly ConcurrentDictionary<string, TimeSpan> Expected = new();

    public static void Register(string job, TimeSpan interval) => Expected[job] = interval;

    public static void Report(string job, TimeSpan interval)
    {
        Expected[job] = interval;
        Beats[job] = new Beat(DateTimeOffset.UtcNow, interval);
    }

    public static IEnumerable<(string Job, TimeSpan Interval, DateTimeOffset? LastAt)> All() =>
        Expected.Select(e => (e.Key, e.Value, Beats.TryGetValue(e.Key, out var b) ? b.At : (DateTimeOffset?)null));

    /// <summary>Late once it misses two runs plus a little slack; critical after three.</summary>
    public static (TimeSpan Warn, TimeSpan Crit) Limits(TimeSpan interval) =>
        (interval * 2 + TimeSpan.FromMinutes(2), interval * 3 + TimeSpan.FromMinutes(5));
}
