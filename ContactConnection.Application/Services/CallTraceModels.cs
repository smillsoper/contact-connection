namespace ContactConnection.Application.Services;

/// <summary>Server-enforced backstops against a runaway/forgotten trace — apply regardless of what was requested.</summary>
public static class CallTraceLimits
{
    public const int MaxCaptureCount = 500;
    public static readonly TimeSpan MaxCaptureDuration = TimeSpan.FromMinutes(60);
}

public static class CallTraceCaptureMode
{
    public const string Count = "count";
    public const string Duration = "duration";
}

/// <summary>
/// Filter + capture cap for a new live trace subscription. Null filter fields mean "all".
/// CaptureValue is calls (CaptureMode = Count) or minutes (CaptureMode = Duration) — already
/// clamped to CallTraceLimits by the caller before reaching the registry.
/// </summary>
public class StartTraceRequest
{
    public required Guid TenantId { get; init; }
    public Guid? CampaignId { get; init; }
    public Guid? FlowId { get; init; }
    public string? Dnis { get; init; }
    public string? Ani { get; init; }
    public required string CaptureMode { get; init; }
    public required int CaptureValue { get; init; }
}

public class CallTraceSubscriptionInfo
{
    public required Guid SubscriptionId { get; init; }
    public required Guid TenantId { get; init; }
    public Guid? CampaignId { get; init; }
    public Guid? FlowId { get; init; }
    public string? Dnis { get; init; }
    public string? Ani { get; init; }
    public required string CaptureMode { get; init; }
    public required int CaptureValue { get; init; }
    public required int MatchedCount { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}

public class CallTraceStepDto
{
    public required Guid CallRecordId { get; init; }
    public required int Sequence { get; init; }
    public required string Engine { get; init; }
    public required string NodeId { get; init; }
    public required string NodeType { get; init; }
    public string? Label { get; init; }
    public required DateTimeOffset EnteredAt { get; init; }
    public string? Detail { get; init; }
    public string? TransitionTaken { get; init; }
    public string? ExitReason { get; init; }
    public string? NextNodeId { get; init; }

    /// <summary>JSON snapshot of call/flow variable state at the end of this step — sensitive
    /// values already redacted server-side before this was built.</summary>
    public string? StateSnapshot { get; init; }
}
