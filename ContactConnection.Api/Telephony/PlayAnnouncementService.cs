using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Drives tf_play periodic ("intermittent") announcements over looping hold audio.
///
/// PlayNodeHandler only seeds the session vars (<c>_play_announcements_json</c>,
/// <c>_play_announcement_interval</c>, …). The timing can't hang off the PLAYBACK_STOP boundary the
/// way the rest of the play loop does: a looping MOH source (<c>local_stream://moh</c>, or a
/// multi-minute music file) fires PLAYBACK_STOP rarely or never, so any interval shorter than the
/// track length would simply never come due. This service ticks every couple of seconds and, when
/// an announcement is due for a live looping play, breaks the MOH and broadcasts the next playlist
/// entry (a bare uuid_broadcast alone does NOT pre-empt an in-progress uuid_broadcast-injected
/// playback on a parked channel — it queues behind it — so the uuid_break first is required).
/// EslBackgroundService.HandlePlaybackStopAsync then resumes the MOH once the announcement finishes
/// and advances the playlist index — it tells the announcement's own PLAYBACK_STOP apart from the
/// MOH-break PLAYBACK_STOP by file path (with a short time-guard fallback), keyed off the
/// <c>_play_state="announcement"</c> / <c>_play_announcement_fired_at</c> this service stamps.
/// </summary>
public sealed class PlayAnnouncementService : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);

    private readonly ITelephonyCallSessionStore _sessionStore;
    private readonly IConfiguration _config;
    private readonly ILogger<PlayAnnouncementService> _logger;
    private readonly ILogger<EslClient> _eslLogger;

    public PlayAnnouncementService(
        ITelephonyCallSessionStore sessionStore,
        IConfiguration config,
        ILogger<PlayAnnouncementService> logger,
        ILogger<EslClient> eslLogger)
    {
        _sessionStore = sessionStore;
        _config       = config;
        _logger       = logger;
        _eslLogger    = eslLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "PlayAnnouncementService: unhandled error during tick");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var sessions = await _sessionStore.GetAllAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var due = sessions
            .Select(s => (Session: s, Ann: DueAnnouncement(s, now)))
            .Where(x => x.Ann is not null)
            .ToList();
        if (due.Count == 0) return;

        var host = _config["FreeSWITCH:Host"] ?? "127.0.0.1";
        var port = int.Parse(_config["FreeSWITCH:EslPort"] ?? "8021");
        var pass = _config["FreeSWITCH:EslPassword"] ?? "ClueCon";

        await using var esl = new EslClient(_eslLogger);
        await esl.ConnectAsync(host, port, pass, ct);

        foreach (var (session, ann) in due)
        {
            // Re-read immediately before acting — the scan above raced the ~2 s tick, and the play
            // may have been bridged / ended / already interrupted since.
            var fresh = await _sessionStore.GetAsync(session.ChannelUuid, ct);
            if (fresh is null || DueAnnouncement(fresh, DateTimeOffset.UtcNow) is null) continue;

            var stamp = DateTimeOffset.UtcNow.ToString("O");
            fresh.Vars["_play_state"]                = "announcement";
            fresh.Vars["_play_last_announcement_at"] = stamp;
            fresh.Vars["_play_announcement_fired_at"] = stamp;
            await _sessionStore.SaveAsync(fresh, ct);

            // Break the in-progress MOH, then broadcast the announcement. The break's PLAYBACK_STOP
            // is recognised as a MOH interrupt (not the announcement finishing) by
            // EslBackgroundService.HandlePlaybackStopAsync because _play_state is now "announcement".
            await esl.BreakChannelAsync(session.ChannelUuid, ct);
            await esl.BroadcastAsync(session.ChannelUuid, ann!, ct);

            _logger.LogInformation(
                "PlayAnnouncement [{Uuid}]: playing announcement '{Arg}' over hold audio", session.ChannelUuid, ann);
        }
    }

    /// <summary>
    /// The playlist entry that should play now for this session, or null if none is due. Pure /
    /// static so it's unit-testable and can be re-checked cheaply against a freshly-read session.
    /// </summary>
    internal static string? DueAnnouncement(TelephonyCallSession s, DateTimeOffset now)
    {
        if (s.Vars.GetValueOrDefault("_play_loop") != "true") return null;
        if (s.Vars.GetValueOrDefault("_play_state", "main") != "main") return null; // mid-announcement / streaming

        var json = s.Vars.GetValueOrDefault("_play_announcements_json", "");
        if (string.IsNullOrEmpty(json)) return null;

        if (!int.TryParse(s.Vars.GetValueOrDefault("_play_announcement_interval", "30"), out var interval) || interval <= 0)
            return null;

        var anchorRaw = s.Vars.GetValueOrDefault("_play_last_announcement_at", "");
        if (string.IsNullOrEmpty(anchorRaw)) anchorRaw = s.Vars.GetValueOrDefault("_play_started_at", "");
        if (!DateTimeOffset.TryParse(anchorRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var anchor))
            anchor = DateTimeOffset.MinValue;
        if ((now - anchor).TotalSeconds < interval) return null;

        List<string>? list;
        try { list = JsonSerializer.Deserialize<List<string>>(json); }
        catch { return null; }
        if (list is null || list.Count == 0) return null;

        var idx = int.TryParse(s.Vars.GetValueOrDefault("_play_announcement_index", "0"), out var i) ? i : 0;
        return list[((idx % list.Count) + list.Count) % list.Count];
    }
}
