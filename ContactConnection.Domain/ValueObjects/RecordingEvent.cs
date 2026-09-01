namespace ContactConnection.Domain.ValueObjects;

/// <summary>
/// One start / stop / mask / unmask transition in a call's recording lifecycle.
/// Appended to call_records.recording_events. The ordered list is three things at once:
///   • the PCI compliance audit trail (who masked what, when, why),
///   • the edit-decision-list the A/V merge / sync player consumes,
///   • the source of the denormalised recording_* scalar columns on the call record.
/// Every <see cref="At"/> is a server clock reading (the ESL service's NTP-disciplined
/// host — never the FreeSWITCH channel clock), so screen-capture alignment has one
/// trustworthy time base. See ARCHITECTURE.md §13 / §14.
/// </summary>
public class RecordingEvent
{
    /// <summary>start | stop | mask | unmask — see <see cref="RecordingEventAction"/>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Server timestamp, millisecond precision, UTC.</summary>
    public DateTimeOffset At { get; set; }

    /// <summary>What triggered this transition — see <see cref="RecordingEventSource"/>.</summary>
    public string Source { get; set; } = RecordingEventSource.FlowNode;

    /// <summary>The tf_record node that issued it, when a flow node did.</summary>
    public string? NodeId { get; set; }

    /// <summary>Free-text reason ("payment_field", "ssn", "agent_hold", …). Audit context.</summary>
    public string? Reason { get; set; }

    /// <summary>FreeSWITCH recording file path — set on the <c>start</c> event.</summary>
    public string? RecordingPath { get; set; }

    /// <summary>How a mask window is filled — see <see cref="MaskFillKind"/>. Set on <c>mask</c>.</summary>
    public string? MaskFill { get; set; }

    /// <summary>For field-focus masks driven by the browser extension.</summary>
    public string? FrameUrl { get; set; }

    public static RecordingEvent Start(DateTimeOffset at, string source, string? nodeId, string recordingPath) => new()
    {
        Action = RecordingEventAction.Start, At = at, Source = source, NodeId = nodeId, RecordingPath = recordingPath
    };

    public static RecordingEvent Stop(DateTimeOffset at, string source, string? nodeId = null, string? reason = null) => new()
    {
        Action = RecordingEventAction.Stop, At = at, Source = source, NodeId = nodeId, Reason = reason
    };

    public static RecordingEvent Mask(
        DateTimeOffset at, string source, string maskFill,
        string? nodeId = null, string? reason = null, string? frameUrl = null) => new()
    {
        Action = RecordingEventAction.Mask, At = at, Source = source,
        MaskFill = MaskFillKind.IsValid(maskFill) ? maskFill : MaskFillKind.Silence,
        NodeId = nodeId, Reason = reason, FrameUrl = frameUrl
    };

    public static RecordingEvent Unmask(
        DateTimeOffset at, string source, string? nodeId = null, string? reason = null) => new()
    {
        Action = RecordingEventAction.Unmask, At = at, Source = source, NodeId = nodeId, Reason = reason
    };
}

public static class RecordingEventAction
{
    public const string Start  = "start";
    public const string Stop   = "stop";
    public const string Mask   = "mask";
    public const string Unmask = "unmask";

    public static bool IsValid(string value) =>
        value is Start or Stop or Mask or Unmask;
}

public static class RecordingEventSource
{
    /// <summary>A tf_record node in the telephony flow.</summary>
    public const string FlowNode = "flow_node";
    /// <summary>A custom event raised from the CRM script flow (tf_on_custom_event → tf_record).</summary>
    public const string CustomEvent = "custom_event";
    /// <summary>Browser-extension payment/sensitive field focus detection.</summary>
    public const string FieldFocus = "field_focus";
    /// <summary>Agent pressed a manual mask / stop control.</summary>
    public const string Manual = "manual";
    /// <summary>Automatic mask because the agent placed the caller on hold.</summary>
    public const string AutoHold = "auto_hold";
    /// <summary>The max-mask-duration watchdog forced an unmask.</summary>
    public const string Watchdog = "watchdog";
    /// <summary>Forced stop / unmask during call-disconnect cleanup.</summary>
    public const string Disconnect = "disconnect";

    public static bool IsValid(string value) =>
        value is FlowNode or CustomEvent or FieldFocus or Manual or AutoHold or Watchdog or Disconnect;
}

public static class MaskFillKind
{
    /// <summary>Pure silence — the standard PCI fill.</summary>
    public const string Silence = "silence";
    /// <summary>A faint periodic tone so a QA reviewer sees the gap is intentional, not a dropout.</summary>
    public const string Tone = "tone";
    /// <summary>Low-level comfort noise.</summary>
    public const string ComfortNoise = "comfort_noise";

    public static bool IsValid(string value) =>
        value is Silence or Tone or ComfortNoise;
}
