namespace ContactConnection.Domain.Entities;

/// <summary>
/// One state transition in a call's queue/routing lifecycle (pre_queue → in_queue → routing →
/// active → post_agent → completed, or an abandoned branch). Append-only; never mutated.
/// Duration of a given row = next row's EnteredAt minus this row's (or now() for the latest row).
/// A call can re-enter in_queue under a different CampaignId (e.g. overflow/retarget), so
/// CampaignId is carried per-row rather than only on the CallRecord.
/// </summary>
public class CallStateHistoryEntry
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CallRecordId { get; private set; }
    public int Sequence { get; private set; }
    public string State { get; private set; } = string.Empty;
    public Guid CampaignId { get; private set; }
    public Guid? AgentId { get; private set; }
    public string? Detail { get; private set; }

    /// <summary>Only set when State == CallHistoryState.Abandoned.</summary>
    public string? AbandonType { get; private set; }

    /// <summary>Only set when AbandonType == AbandonType.InQueue.</summary>
    public string? AbandonLength { get; private set; }

    /// <summary>Whether this call was answered within Campaign.ServiceLevelThresholdSeconds of
    /// entering the queue. Null when not applicable (e.g. the call never queued — a direct-
    /// extension bridge — or this row isn't the Active/answered transition).</summary>
    public bool? MetServiceLevel { get; private set; }

    public DateTimeOffset EnteredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CallStateHistoryEntry() { }

    public static CallStateHistoryEntry Create(
        Guid tenantId,
        Guid callRecordId,
        int sequence,
        string state,
        Guid campaignId,
        Guid? agentId,
        string? detail,
        string? abandonType,
        string? abandonLength,
        bool? metServiceLevel = null)
    {
        return new CallStateHistoryEntry
        {
            Id              = Guid.NewGuid(),
            TenantId        = tenantId,
            CallRecordId    = callRecordId,
            Sequence        = sequence,
            State           = state,
            CampaignId      = campaignId,
            AgentId         = agentId,
            Detail          = detail,
            AbandonType     = abandonType,
            AbandonLength   = abandonLength,
            MetServiceLevel = metServiceLevel,
            EnteredAt       = DateTimeOffset.UtcNow,
            CreatedAt       = DateTimeOffset.UtcNow,
        };
    }
}

public static class CallHistoryState
{
    public const string PreQueue  = "pre_queue";
    public const string InQueue   = "in_queue";
    public const string Routing   = "routing";
    public const string Active    = "active";
    public const string PostAgent = "post_agent";
    public const string Completed = "completed";
    public const string Abandoned = "abandoned";
}

public static class CallAbandonType
{
    public const string PreQueue        = "pre_queue";
    public const string InQueue         = "in_queue";
    public const string CallbackAbandon = "callback_abandon";

    /// <summary>Evicted from queue after waiting past Campaign.QueueTimeoutSeconds.</summary>
    public const string QueueTimeout = "queue_timeout";

    /// <summary>Rejected from entering the queue — Campaign.MaxQueueSize already reached.</summary>
    public const string QueueFull = "queue_full";
}

public static class CallAbandonLength
{
    public const string Short = "short";
    public const string Long  = "long";
}
