using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class AgentGroupMemberCampaignExclusionConfiguration : IEntityTypeConfiguration<AgentGroupMemberCampaignExclusion>
{
    public void Configure(EntityTypeBuilder<AgentGroupMemberCampaignExclusion> builder)
    {
        builder.ToTable("agent_group_member_campaign_exclusions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.GroupId).HasColumnName("group_id");
        builder.Property(e => e.AgentId).HasColumnName("agent_id");
        builder.Property(e => e.CampaignId).HasColumnName("campaign_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(e => new { e.GroupId, e.AgentId, e.CampaignId }).IsUnique();
        // The ranker asks "which members of group G are excluded from campaign C".
        builder.HasIndex(e => new { e.CampaignId, e.GroupId });
    }
}
