using ContactConnection.Domain.ValueObjects;

namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// The one chokepoint for call-recording mechanics: issues <c>uuid_record</c> on the caller
/// leg, appends a server-timestamped <see cref="RecordingEvent"/> to the call record, and
/// runs the auto-unmask watchdog so a dropped unmask can't blackhole the rest of a call.
///
/// This is pure mechanism — it does what it's told. Policy (is recording permitted on this
/// campaign, is the call bridged yet, has consent been given) is enforced one layer up, by
/// the tf_record node and any endpoint caller. All four actions use <c>mask</c>/<c>unmask</c>
/// semantics, never stop/start, for sensitive segments — the recording file stays wall-clock
/// continuous so it aligns with the screen capture. See ARCHITECTURE.md §13 / §14.
/// </summary>
public interface ICallRecordingController
{
    /// <summary>Begins recording the channel. Sets RECORD_STEREO first when <paramref name="options"/> asks for it.</summary>
    Task<RecordingActionOutcome> StartAsync(
        RecordingCommand command, RecordingStartOptions options, IEslCommander? esl = null, CancellationToken ct = default);

    /// <summary>Stops recording and cancels any pending mask watchdog for the channel.</summary>
    Task<RecordingActionOutcome> StopAsync(
        RecordingCommand command, IEslCommander? esl = null, CancellationToken ct = default);

    /// <summary>Masks the recording with silence (or a tone) and arms the auto-unmask watchdog.</summary>
    Task<RecordingActionOutcome> MaskAsync(
        RecordingMaskCommand command, IEslCommander? esl = null, CancellationToken ct = default);

    /// <summary>Unmasks the recording and disarms the watchdog.</summary>
    Task<RecordingActionOutcome> UnmaskAsync(
        RecordingCommand command, IEslCommander? esl = null, CancellationToken ct = default);

    /// <summary>
    /// Drops any in-memory watchdog state for a channel without issuing ESL commands. Call from
    /// call-disconnect cleanup once the channel is already gone (a forced unmask would just log
    /// an error against a dead UUID).
    /// </summary>
    void ForgetChannel(string channelUuid);

    /// <summary>
    /// Closes the recording audit trail on hangup: forgets the channel and appends a
    /// <c>stop</c> event (source <c>disconnect</c>). No ESL — FreeSWITCH already stopped the
    /// physical recording when the channel died. Call only when a recording was started and
    /// no stop has been recorded yet (a <c>tf_record(stop)</c> in the disconnect branch, if
    /// wired, gets there first and makes this a no-op the caller skips).
    /// </summary>
    Task FinalizeOnDisconnectAsync(RecordingCommand command, CancellationToken ct = default);
}

/// <summary>Everything the controller needs to act on one channel's recording and record the audit event.</summary>
public record RecordingCommand
{
    public required string ChannelUuid { get; init; }
    public required Guid CallRecordId { get; init; }
    public required string TenantSchemaName { get; init; }

    /// <summary>Who/what triggered this — a <see cref="RecordingEventSource"/> value.</summary>
    public required string Source { get; init; }

    /// <summary>The tf_record node id, when a flow node triggered it.</summary>
    public string? NodeId { get; init; }

    /// <summary>Free-text audit context ("payment_field", "agent_hold", …).</summary>
    public string? Reason { get; init; }
}

/// <summary>Recording knobs that must be applied at <c>start</c> time.</summary>
public record RecordingStartOptions
{
    /// <summary>Record caller and agent on separate channels (<c>RECORD_STEREO=true</c>).</summary>
    public bool Stereo { get; init; } = true;

    /// <summary>Hard cap on the recording's length in seconds; 0 = unlimited.</summary>
    public int LimitSeconds { get; init; }
}

/// <summary>A <see cref="RecordingCommand"/> plus the mask-specific fields.</summary>
public record RecordingMaskCommand : RecordingCommand
{
    /// <summary>How the masked span is filled — a <see cref="MaskFillKind"/> value.</summary>
    public string MaskFill { get; init; } = MaskFillKind.Silence;

    /// <summary>Frame URL for extension-driven field-focus masks.</summary>
    public string? FrameUrl { get; init; }

    /// <summary>
    /// Override for the auto-unmask watchdog timeout. Null uses the configured default
    /// (<c>Recording:MaxMaskSeconds</c>, default 180).
    /// </summary>
    public int? MaxMaskSeconds { get; init; }
}

/// <summary>Result of one recording action.</summary>
public record RecordingActionOutcome(bool Ok, string? Error = null, RecordingEvent? Event = null)
{
    public static RecordingActionOutcome Success(RecordingEvent evt) => new(true, null, evt);
    public static RecordingActionOutcome Failure(string error) => new(false, error, null);
}
