using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class AgentCampaignAssignmentConfiguration : IEntityTypeConfiguration<AgentCampaignAssignment>
{
    public void Configure(EntityTypeBuilder<AgentCampaignAssignment> builder)
    {
        builder.ToTable("agent_campaign_assignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(a => a.CampaignId).HasColumnName("campaign_id").IsRequired();
        builder.Property(a => a.Proficiency).HasColumnName("proficiency");
        builder.Property(a => a.IsActive).HasColumnName("is_active");
        builder.Property(a => a.AssignedAt).HasColumnName("assigned_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        // One active assignment per agent per campaign
        builder.HasIndex(a => new { a.AgentId, a.CampaignId }).IsUnique();
        builder.HasIndex(a => a.CampaignId);
        // Queue builder sorts by proficiency DESC — index supports this
        builder.HasIndex(a => new { a.CampaignId, a.IsActive, a.Proficiency });
    }
}
