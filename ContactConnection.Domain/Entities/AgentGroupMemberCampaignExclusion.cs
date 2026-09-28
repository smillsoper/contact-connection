namespace ContactConnection.Domain.Entities;

/// <summary>
/// "This group member may NOT take this campaign through this group." A group assigned several
/// campaigns (e.g. Life Seasons' Alpha team: NeuroQ TV, NeuroQ SF TV, My Best Heart, Joint Food) lets
/// each member take all of them by default; an exclusion removes one campaign for one member — the
/// replacement for CXone's per-agent routing attributes. Stored as exclusions (not an allow-list)
/// so newly added members and newly assigned campaigns need no re-syncing.
/// </summary>
public class AgentGroupMemberCampaignExclusion
{
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid CampaignId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AgentGroupMemberCampaignExclusion() { }

    public static AgentGroupMemberCampaignExclusion Create(Guid groupId, Guid agentId, Guid campaignId) => new()
    {
        Id = Guid.NewGuid(),
        GroupId = groupId,
        AgentId = agentId,
        CampaignId = campaignId,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
