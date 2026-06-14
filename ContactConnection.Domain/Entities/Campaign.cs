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

    // The flow agents run for calls in this campaign.
    public Guid? FlowId { get; private set; }

    // Queue behaviour
    public int MaxQueueSize { get; private set; } = 50;
    public int QueueTimeoutSeconds { get; private set; } = 300;           // abandon after 5 min
    public int ServiceLevelThresholdSeconds { get; private set; } = 30;   // SL target

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

    public void Update(string name, string? description,
        int maxQueueSize, int queueTimeoutSeconds, int serviceLevelThresholdSeconds)
    {
        Name                          = name.Trim();
        Description                   = description?.Trim();
        MaxQueueSize                  = maxQueueSize;
        QueueTimeoutSeconds           = queueTimeoutSeconds;
        ServiceLevelThresholdSeconds  = serviceLevelThresholdSeconds;
        UpdatedAt                     = DateTimeOffset.UtcNow;
    }

    public void AssignFlow(Guid flowId)  { FlowId = flowId; UpdatedAt = DateTimeOffset.UtcNow; }
    public void RemoveFlow()             { FlowId = null;   UpdatedAt = DateTimeOffset.UtcNow; }

    public void Activate()  { Status = CampaignStatus.Active;   UpdatedAt = DateTimeOffset.UtcNow; }
    public void Pause()     { Status = CampaignStatus.Paused;   UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate(){ Status = CampaignStatus.Inactive; UpdatedAt = DateTimeOffset.UtcNow; }
}

public static class CampaignStatus
{
    public const string Active   = "active";
    public const string Paused   = "paused";
    public const string Inactive = "inactive";
}
