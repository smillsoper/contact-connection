namespace ContactConnection.Domain.Entities;

/// <summary>
/// A named group of agents that can be assigned to multiple campaigns.
/// Enables parallel queue coverage — e.g., an "Alpha Sales" group can serve calls
/// from any campaign it is assigned to, alongside that campaign's direct agent assignments.
/// </summary>
public class AgentGroup
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    private readonly List<AgentGroupMember> _members = [];
    public IReadOnlyList<AgentGroupMember> Members => _members.AsReadOnly();

    private readonly List<GroupCampaignAssignment> _campaignAssignments = [];
    public IReadOnlyList<GroupCampaignAssignment> CampaignAssignments => _campaignAssignments.AsReadOnly();

    private AgentGroup() { }

    public static AgentGroup Create(Guid tenantId, string name, string slug, string? description = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentGroup
        {
            Id          = Guid.NewGuid(),
            TenantId    = tenantId,
            Name        = name.Trim(),
            Slug        = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            IsActive    = true,
            CreatedAt   = now,
            UpdatedAt   = now
        };
    }

    public void Update(string name, string? description)
    {
        Name        = name.Trim();
        Description = description?.Trim();
        UpdatedAt   = DateTimeOffset.UtcNow;
    }

    public AgentGroupMember AddMember(Guid agentId)
    {
        var member = AgentGroupMember.Create(Id, agentId);
        _members.Add(member);
        UpdatedAt = DateTimeOffset.UtcNow;
        return member;
    }

    public void Activate()   { IsActive = true;  UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTimeOffset.UtcNow; }
}
