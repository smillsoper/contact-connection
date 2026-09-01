namespace ContactConnection.Domain.ValueObjects;

/// <summary>
/// A single sync anchor on a screen recording's timeline — a call-lifecycle moment (bridge,
/// hold, mask, …) the browser extension learned about over SignalR, expressed as milliseconds
/// from capture start. Gives the A/V merge more than one alignment point beyond t0.
/// </summary>
public class ScreenRecordingCuePoint
{
    public long AtMs { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
