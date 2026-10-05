namespace ContactConnection.Domain.Entities;

/// <summary>
/// Audit trail for every manual outbound dial an agent asks for (S179, Sprint 1 item 1b) — placed or blocked, with the
/// caller ID the server chose and, for a calling-hours decision, the callee time zone and how it was determined.
/// Compliance record: carriers and regulators ask "why did this number get called at this time, as this caller ID".
/// </summary>
public class OutboundDialAttempt
{
    public Guid Id { get; private set; }
    public Guid AgentId { get; private set; }
    /// <summary>The manual outbound campaign dialed under; null for a direct dial.</summary>
    public Guid? CampaignId { get; private set; }
    public string DialedNumber { get; private set; } = string.Empty;
    public string? CallerId { get; private set; }
    /// <summary>placed | blocked | failed (the originate didn't go through).</summary>
    public string Result { get; private set; } = OutboundDialResult.Placed;
    public string? Reason { get; private set; }
    public string? CalleeTimeZone { get; private set; }
    /// <summary>prior_call_state | tenant_default — how the callee's time zone was determined.</summary>
    public string? TimeZoneSource { get; private set; }
    public Guid? CallRecordId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private OutboundDialAttempt() { }

    public static OutboundDialAttempt Create(
        Guid agentId, Guid? campaignId, string dialedNumber, string? callerId, string result, string? reason,
        string? calleeTimeZone, string? timeZoneSource, Guid? callRecordId = null) => new()
    {
        Id = Guid.NewGuid(),
        AgentId = agentId,
        CampaignId = campaignId,
        DialedNumber = dialedNumber,
        CallerId = callerId,
        Result = result,
        Reason = reason,
        CalleeTimeZone = calleeTimeZone,
        TimeZoneSource = timeZoneSource,
        CallRecordId = callRecordId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public void MarkFailed(string reason) { Result = OutboundDialResult.Failed; Reason = reason; }
}

public static class OutboundDialResult
{
    public const string Placed  = "placed";
    public const string Blocked = "blocked";
    public const string Failed  = "failed";
}
