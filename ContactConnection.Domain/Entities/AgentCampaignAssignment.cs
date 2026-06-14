namespace ContactConnection.Domain.Entities;

/// <summary>
/// Direct assignment of an agent to a campaign with a proficiency score.
/// Higher proficiency = higher priority in the queue (sorted DESC when building the eligible-agent list).
/// Range 1–100; default 50 (mid-range).
/// </summary>
public class AgentCampaignAssignment
{
    public Guid Id { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid CampaignId { get; private set; }

    /// <summary>1–100. Higher value = higher queue priority.</summary>
    public int Proficiency { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AgentCampaignAssignment() { }

    public static AgentCampaignAssignment Create(Guid agentId, Guid campaignId, int proficiency = 50)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentCampaignAssignment
        {
            Id          = Guid.NewGuid(),
            AgentId     = agentId,
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
