namespace ContactConnection.Domain.Entities;

/// <summary>
/// A supervisor's note to an agent, usually mid-call (S183): coaching without talking in their ear. Pinned in the
/// agent's portal until they press "Got it"; the supervisor sees Sent → Seen → Got it. Kept against the call it was
/// sent during, so QA sees the coaching on the call's record.
/// </summary>
public class CoachingNote
{
    public const int MaxLength = 500;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid FromId { get; private set; }
    public string FromName { get; private set; } = "";
    public string Text { get; private set; } = "";
    /// <summary>The call the agent was on when it was sent (null: not on a call).</summary>
    public Guid? CallRecordId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SeenAt { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? RetractedAt { get; private set; }

    private CoachingNote() { }

    public static CoachingNote Create(Guid tenantId, Guid agentId, Guid fromId, string fromName, string text, Guid? callRecordId)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) throw new ArgumentException("Write a note first.");
        if (t.Length > MaxLength) throw new ArgumentException($"Keep it under {MaxLength} characters.");
        return new CoachingNote
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AgentId = agentId, FromId = fromId, FromName = fromName, Text = t,
            CallRecordId = callRecordId, CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Still pinned in the agent's portal.</summary>
    public bool IsOpen => AcknowledgedAt is null && RetractedAt is null;

    public string Status => RetractedAt is not null ? "retracted" : AcknowledgedAt is not null ? "acknowledged" : SeenAt is not null ? "seen" : "sent";

    public void Seen() => SeenAt ??= DateTimeOffset.UtcNow;

    public void Acknowledge()
    {
        SeenAt ??= DateTimeOffset.UtcNow;
        AcknowledgedAt ??= DateTimeOffset.UtcNow;
    }

    public void Retract() { if (IsOpen) RetractedAt = DateTimeOffset.UtcNow; }
}
