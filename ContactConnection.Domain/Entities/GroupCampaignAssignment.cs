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

    // ── Parallel queuing (docs/design/parallel-queuing.md) ─────────────────
    /// <summary>Priority tier of this group for this campaign. 0 = regular (the same as direct agent
    /// assignments); higher tiers are offered a waiting call first — e.g. a premium "Alpha" team.
    /// A call waits on every tier at once; the highest tier with an eligible available agent wins.</summary>
    public int RoutingTier { get; private set; }
    /// <summary>Optional: hold a new call for this tier (or higher) exclusively for N seconds even if
    /// lower tiers have agents free. Null = pure priority (the default).</summary>
    public int? ExclusiveWindowSeconds { get; private set; }
    /// <summary>What a call answered through this assignment is tagged with (CallRecord.RoutedTierLabel)
    /// — e.g. "Alpha", "Elite"; the basis for commission reporting and the agent's badge.</summary>
    public string? TierLabel { get; private set; }

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

    public void SetRouting(int routingTier, int? exclusiveWindowSeconds, string? tierLabel)
    {
        if (routingTier is < 0 or > 100)
            throw new ArgumentException("Routing tier must be between 0 and 100.", nameof(routingTier));
        if (exclusiveWindowSeconds is { } w && (w < 1 || w > 600))
            throw new ArgumentException("Exclusive window must be 1–600 seconds.", nameof(exclusiveWindowSeconds));
        if (exclusiveWindowSeconds is not null && routingTier == 0)
            throw new ArgumentException("An exclusive window only applies to a priority tier (above 0).", nameof(exclusiveWindowSeconds));
        RoutingTier            = routingTier;
        ExclusiveWindowSeconds = exclusiveWindowSeconds;
        TierLabel              = string.IsNullOrWhiteSpace(tierLabel) ? null : tierLabel.Trim();
        UpdatedAt              = DateTimeOffset.UtcNow;
    }

    public void SetProficiency(int proficiency)
    {
        Proficiency = Math.Clamp(proficiency, 1, 100);
        UpdatedAt   = DateTimeOffset.UtcNow;
    }

    public void Activate()   { IsActive = true;  UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTimeOffset.UtcNow; }
}
