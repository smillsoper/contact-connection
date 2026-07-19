namespace ContactConnection.Domain.Entities;

public class Campaign
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ClientId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Status { get; private set; } = CampaignStatus.Active;
    public string? Description { get; private set; }

    // The fallback CRM script flow agents run for calls in this campaign.
    public Guid? FlowId { get; private set; }

    // Telephony flow executed by ESL when an inbound call arrives on this campaign's DID.
    public Guid? InboundFlowId { get; private set; }

    // Telephony flow to execute before the agent dials (manual outbound only).
    public Guid? OutboundFlowId { get; private set; }

    // Telephony direction
    public string Direction { get; private set; } = CampaignDirection.Inbound;
    public string DialMode { get; private set; } = CampaignDialMode.Manual;  // outbound only
    public string? CallerIdNumber { get; private set; }   // outbound only

    // Campaign-level routing priority (1–10; higher = preferred when agent eligible for multiple)
    public int Priority { get; private set; } = 5;

    // After-call work time agents must complete before going ready again
    public int AfterCallWorkSeconds { get; private set; } = 30;

    // Queue behaviour
    public int MaxQueueSize { get; private set; } = 50;
    public int QueueTimeoutSeconds { get; private set; } = 300;
    public int ServiceLevelThresholdSeconds { get; private set; } = 30;

    // Calls that hang up while in_queue within this many seconds are a "short" abandon;
    // beyond it, a "long" abandon. Drives abandon-length classification in call state history.
    public int ShortAbandonThresholdSeconds { get; private set; } = 10;

    // Queue acceleration: raise waiting caller's effective priority every N seconds
    public bool QueueAccelerationEnabled { get; private set; }
    public int QueueAccelerationIntervalSeconds { get; private set; } = 60;
    public int QueueAccelerationPriorityBoost { get; private set; } = 1;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    public Client? Client { get; private set; }

    private readonly List<PhoneNumber> _phoneNumbers = [];
    public IReadOnlyList<PhoneNumber> PhoneNumbers => _phoneNumbers.AsReadOnly();

    private readonly List<AgentCampaignAssignment> _agentAssignments = [];
    public IReadOnlyList<AgentCampaignAssignment> AgentAssignments => _agentAssignments.AsReadOnly();

    private readonly List<GroupCampaignAssignment> _groupAssignments = [];
    public IReadOnlyList<GroupCampaignAssignment> GroupAssignments => _groupAssignments.AsReadOnly();

    private readonly List<CampaignExternalNumber> _externalNumbers = [];
    public IReadOnlyList<CampaignExternalNumber> ExternalNumbers => _externalNumbers.AsReadOnly();

    private Campaign() { }

    public static Campaign Create(
        Guid tenantId,
        Guid clientId,
        string name,
        string slug,
        string? description = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Campaign
        {
            Id          = Guid.NewGuid(),
            TenantId    = tenantId,
            ClientId    = clientId,
            Name        = name.Trim(),
            Slug        = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            Status      = CampaignStatus.Active,
            CreatedAt   = now,
            UpdatedAt   = now
        };
    }

    public void Update(
        string name,
        string? description,
        string direction,
        string dialMode,
        int priority,
        int afterCallWorkSeconds,
        string? callerIdNumber,
        int maxQueueSize,
        int queueTimeoutSeconds,
        int serviceLevelThresholdSeconds,
        int shortAbandonThresholdSeconds,
        bool queueAccelerationEnabled,
        int queueAccelerationIntervalSeconds,
        int queueAccelerationPriorityBoost)
    {
        Name                                 = name.Trim();
        Description                          = description?.Trim();
        Direction                            = direction == CampaignDirection.Outbound ? CampaignDirection.Outbound : CampaignDirection.Inbound;
        DialMode                             = Direction == CampaignDirection.Outbound && CampaignDialMode.IsValid(dialMode) ? dialMode : CampaignDialMode.Manual;
        Priority                             = Math.Clamp(priority, 1, 10);
        AfterCallWorkSeconds                 = Math.Max(0, afterCallWorkSeconds);
        CallerIdNumber                       = callerIdNumber?.Trim();
        MaxQueueSize                         = Math.Max(1, maxQueueSize);
        QueueTimeoutSeconds                  = Math.Max(0, queueTimeoutSeconds);
        ServiceLevelThresholdSeconds         = Math.Max(0, serviceLevelThresholdSeconds);
        ShortAbandonThresholdSeconds         = Math.Max(0, shortAbandonThresholdSeconds);
        QueueAccelerationEnabled             = queueAccelerationEnabled;
        QueueAccelerationIntervalSeconds     = Math.Max(1, queueAccelerationIntervalSeconds);
        QueueAccelerationPriorityBoost       = Math.Max(1, queueAccelerationPriorityBoost);
        UpdatedAt                            = DateTimeOffset.UtcNow;
    }

    public void AssignFlow(Guid flowId)         { FlowId = flowId;         UpdatedAt = DateTimeOffset.UtcNow; }
    public void RemoveFlow()                    { FlowId = null;           UpdatedAt = DateTimeOffset.UtcNow; }
    public void AssignInboundFlow(Guid flowId)  { InboundFlowId = flowId;  UpdatedAt = DateTimeOffset.UtcNow; }
    public void RemoveInboundFlow()             { InboundFlowId = null;    UpdatedAt = DateTimeOffset.UtcNow; }
    public void AssignOutboundFlow(Guid flowId) { OutboundFlowId = flowId; UpdatedAt = DateTimeOffset.UtcNow; }
    public void RemoveOutboundFlow()            { OutboundFlowId = null;   UpdatedAt = DateTimeOffset.UtcNow; }

    public void Activate()   { Status = CampaignStatus.Active;   UpdatedAt = DateTimeOffset.UtcNow; }
    public void Pause()      { Status = CampaignStatus.Paused;   UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { Status = CampaignStatus.Inactive; UpdatedAt = DateTimeOffset.UtcNow; }
}

public static class CampaignStatus
{
    public const string Active   = "active";
    public const string Paused   = "paused";
    public const string Inactive = "inactive";
}

public static class CampaignDirection
{
    public const string Inbound  = "inbound";
    public const string Outbound = "outbound";
}

public static class CampaignDialMode
{
    public const string Manual      = "manual";
    public const string Progressive = "progressive";
    public const string Predictive  = "predictive";

    public static bool IsValid(string value) =>
        value is Manual or Progressive or Predictive;
}
