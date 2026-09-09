using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Plays the periodic call-recording notification beep. Every <c>Recording:BeepIntervalSeconds</c>
/// (default 15) it asks <see cref="ICallRecordingController.BeepingChannels"/> which live recordings
/// want it and fires a short <c>uuid_broadcast … both</c> tone on each — overlaying the live
/// conversation so caller and agent both hear it, and (because it's on the channel) it's captured
/// on the recording as proof the notification played.
///
/// A dedicated service rather than <c>uuid_displace</c> from CallRecordingController: displace only
/// mixes into one direction, so the far party never heard it.
/// </summary>
public sealed class RecordingBeepService(
    ICallRecordingController recording,
    IConfiguration config,
    ILogger<RecordingBeepService> logger,
    ILogger<EslClient> eslLogger) : BackgroundService
{
    private TimeSpan Interval =>
        TimeSpan.FromSeconds(int.TryParse(config["Recording:BeepIntervalSeconds"], out var s) && s > 0 ? s : 15);

    // 250ms of 1400Hz, no trailing silence — a single short blip per tick.
    private string Tone => config["Recording:BeepTone"] is { Length: > 0 } t ? t : "tone_stream://%(250,0,1400)";

    private string EslHost => config["FreeSWITCH:Host"] ?? "127.0.0.1";
    private int    EslPort => int.TryParse(config["FreeSWITCH:EslPort"], out var p) ? p : 8021;
    private string EslPass => config["FreeSWITCH:EslPassword"] ?? "ClueCon";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Interval;
        var tone = Tone;
        logger.LogInformation("RecordingBeepService started — every {Interval}s, tone {Tone}.", interval.TotalSeconds, tone);

        EslClient? esl = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) { break; }

                var channels = recording.BeepingChannels();
                if (channels.Count == 0) continue;

                try
                {
                    if (esl is null)
                    {
                        esl = new EslClient(eslLogger);
                        await esl.ConnectAsync(EslHost, EslPort, EslPass, stoppingToken);
                    }

                    foreach (var uuid in channels)
                        await esl.BroadcastToBothLegsAsync(uuid, tone, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "RecordingBeepService: beep tick failed — dropping the ESL connection, will reconnect.");
                    if (esl is not null) { await esl.DisposeAsync(); esl = null; }
                }
            }
        }
        finally
        {
            if (esl is not null) await esl.DisposeAsync();
        }

        logger.LogInformation("RecordingBeepService stopped.");
    }
}
