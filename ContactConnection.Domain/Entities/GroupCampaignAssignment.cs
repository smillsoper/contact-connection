namespace ContactConnection.Domain.Entities;

/// <summary>
/// Assignment of an agent group to a campaign with a group-level proficiency score.
/// All agents in the group inherit this proficiency for the campaign's queue.
/// When building the eligible-agent list, individual agents (direct assignments) and
/// group members are merged and sorted by their effective proficiency DESC.
/// </summary>
public class GroupCampaignAssignment
{
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid CampaignId { get; private set; }

    /// <summary>1–100. Controls where group members sit relative to directly-assigned agents.</summary>
    public int Proficiency { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    public AgentGroup? Group { get; private set; }

    private GroupCampaignAssignment() { }

    public static GroupCampaignAssignment Create(Guid groupId, Guid campaignId, int proficiency = 50)
    {
        var now = DateTimeOffset.UtcNow;
        return new GroupCampaignAssignment
        {
            Id          = Guid.NewGuid(),
            GroupId     = groupId,
            CampaignId  = campaignId,
            Proficiency = Math.Clamp(proficiency, 1, 100),
            IsActive    = true,
            AssignedAt  = now,
            UpdatedAt   = now
        };
    }

    public void SetProficiency(int proficiency)
    {
        Proficiency = Math.Clamp(proficiency, 1, 100);
        UpdatedAt   = DateTimeOffset.UtcNow;
    }

    public void Activate()   { IsActive = true;  UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTimeOffset.UtcNow; }
}
