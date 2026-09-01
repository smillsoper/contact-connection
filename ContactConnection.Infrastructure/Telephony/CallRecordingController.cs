using System.Collections.Concurrent;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// The one chokepoint for call-recording mechanics — see <see cref="ICallRecordingController"/>.
/// Singleton: it owns the in-memory auto-unmask watchdog timers. Every timestamp it records is
/// taken from this host's clock (the API/ESL host, NTP-disciplined via the cc_timesync sidecar),
/// which is the single time base the screen-capture merge aligns against.
/// </summary>
public sealed class CallRecordingController : ICallRecordingController
{
    private readonly IEslCommanderFactory _eslFactory;
    private readonly ICallRecordingRepository _repository;
    private readonly ILogger<CallRecordingController> _logger;

    private readonly string _recordingsBase;
    private readonly int _defaultMaxMaskSeconds;

    // channelUuid -> the CTS that cancels its pending forced-unmask
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _watchdogs = new();

    public CallRecordingController(
        IEslCommanderFactory eslFactory,
        ICallRecordingRepository repository,
        IConfiguration config,
        ILogger<CallRecordingController> logger)
    {
        _eslFactory = eslFactory;
        _repository = repository;
        _logger     = logger;

        _recordingsBase = (config["FreeSWITCH:RecordingsContainerPath"] ?? "/var/lib/freeswitch/recordings").TrimEnd('/');
        _defaultMaxMaskSeconds =
            int.TryParse(config["Recording:MaxMaskSeconds"], out var v) && v > 0 ? v : 180;
    }

    public Task<RecordingActionOutcome> StartAsync(
        RecordingCommand command, RecordingStartOptions options, IEslCommander? esl = null, CancellationToken ct = default)
        => RunAsync(esl, async e =>
        {
            var path = PathFor(command);
            if (options.Stereo)
                await e.SetChannelVarAsync(command.ChannelUuid, "RECORD_STEREO", "true", ct);

            await e.RecordAsync(command.ChannelUuid, RecordingEventAction.Start, path, options.LimitSeconds, ct);

            var evt = RecordingEvent.Start(UtcNow(), command.Source, command.NodeId, path);
            await PersistAsync(command, evt, ct);
            _logger.LogInformation(
                "Recording started [{Uuid}] call={CallRecordId} stereo={Stereo} path={Path} source={Source}",
                command.ChannelUuid, command.CallRecordId, options.Stereo, path, command.Source);
            return RecordingActionOutcome.Success(evt);
        });

    public Task<RecordingActionOutcome> StopAsync(
        RecordingCommand command, IEslCommander? esl = null, CancellationToken ct = default)
    {
        DisarmWatchdog(command.ChannelUuid);
        return RunAsync(esl, async e =>
        {
            await e.RecordAsync(command.ChannelUuid, RecordingEventAction.Stop, PathFor(command), 0, ct);
            var evt = RecordingEvent.Stop(UtcNow(), command.Source, command.NodeId, command.Reason);
            await PersistAsync(command, evt, ct);
            _logger.LogInformation("Recording stopped [{Uuid}] call={CallRecordId} source={Source}",
                command.ChannelUuid, command.CallRecordId, command.Source);
            return RecordingActionOutcome.Success(evt);
        });
    }

    public Task<RecordingActionOutcome> MaskAsync(
        RecordingMaskCommand command, IEslCommander? esl = null, CancellationToken ct = default)
        => RunAsync(esl, async e =>
        {
            await e.RecordAsync(command.ChannelUuid, RecordingEventAction.Mask, PathFor(command), 0, ct);

            var fill = MaskFillKind.IsValid(command.MaskFill) ? command.MaskFill : MaskFillKind.Silence;
            var evt = RecordingEvent.Mask(
                UtcNow(), command.Source, fill, command.NodeId, command.Reason, command.FrameUrl);
            await PersistAsync(command, evt, ct);

            var timeout = command.MaxMaskSeconds is > 0 ? command.MaxMaskSeconds.Value : _defaultMaxMaskSeconds;
            ArmWatchdog(command, timeout);
            _logger.LogInformation(
                "Recording masked [{Uuid}] call={CallRecordId} fill={Fill} source={Source} watchdog={Timeout}s",
                command.ChannelUuid, command.CallRecordId, fill, command.Source, timeout);
            return RecordingActionOutcome.Success(evt);
        });

    public Task<RecordingActionOutcome> UnmaskAsync(
        RecordingCommand command, IEslCommander? esl = null, CancellationToken ct = default)
    {
        DisarmWatchdog(command.ChannelUuid);
        return RunAsync(esl, async e =>
        {
            await e.RecordAsync(command.ChannelUuid, RecordingEventAction.Unmask, PathFor(command), 0, ct);
            var evt = RecordingEvent.Unmask(UtcNow(), command.Source, command.NodeId, command.Reason);
            await PersistAsync(command, evt, ct);
            _logger.LogInformation("Recording unmasked [{Uuid}] call={CallRecordId} source={Source}",
                command.ChannelUuid, command.CallRecordId, command.Source);
            return RecordingActionOutcome.Success(evt);
        });
    }

    public void ForgetChannel(string channelUuid) => DisarmWatchdog(channelUuid);

    public async Task FinalizeOnDisconnectAsync(RecordingCommand command, CancellationToken ct = default)
    {
        DisarmWatchdog(command.ChannelUuid);
        var evt = RecordingEvent.Stop(
            UtcNow(), RecordingEventSource.Disconnect, command.NodeId, command.Reason ?? "call_disconnected");
        await PersistAsync(command, evt, ct);
        _logger.LogInformation(
            "Recording finalised on disconnect [{Uuid}] call={CallRecordId}", command.ChannelUuid, command.CallRecordId);
    }

    // ── internals ───────────────────────────────────────────────────────────

    private string PathFor(RecordingCommand c) => $"{_recordingsBase}/{c.CallRecordId}.wav";

    private static DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;

    private async Task<RecordingActionOutcome> RunAsync(
        IEslCommander? esl, Func<IEslCommander, Task<RecordingActionOutcome>> body)
    {
        try
        {
            if (esl is not null)
                return await body(esl);

            await using var owned = await _eslFactory.CreateAsync();
            return await body(owned);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Call recording action failed");
            return RecordingActionOutcome.Failure(ex.Message);
        }
    }

    private async Task PersistAsync(RecordingCommand c, RecordingEvent evt, CancellationToken ct)
    {
        var persisted = await _repository.AppendEventAsync(c.TenantSchemaName, c.CallRecordId, evt, ct);
        if (!persisted)
            _logger.LogWarning(
                "Recording {Action} on channel {Uuid} issued, but call record {CallRecordId} was not found — audit event not persisted",
                evt.Action, c.ChannelUuid, c.CallRecordId);
    }

    private void ArmWatchdog(RecordingMaskCommand command, int seconds)
    {
        DisarmWatchdog(command.ChannelUuid);

        var cts = new CancellationTokenSource();
        _watchdogs[command.ChannelUuid] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return; // unmasked / stopped in time
            }

            _logger.LogWarning(
                "Recording mask watchdog fired for channel {Uuid} (call {CallRecordId}) after {Seconds}s — forcing unmask",
                command.ChannelUuid, command.CallRecordId, seconds);

            await UnmaskAsync(new RecordingCommand
            {
                ChannelUuid      = command.ChannelUuid,
                CallRecordId     = command.CallRecordId,
                TenantSchemaName = command.TenantSchemaName,
                Source           = RecordingEventSource.Watchdog,
                Reason           = "max_mask_duration_exceeded",
            }, esl: null, CancellationToken.None);
        });
    }

    private void DisarmWatchdog(string channelUuid)
    {
        if (_watchdogs.TryRemove(channelUuid, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
