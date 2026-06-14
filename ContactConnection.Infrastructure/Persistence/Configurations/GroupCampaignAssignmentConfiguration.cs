using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class GroupCampaignAssignmentConfiguration : IEntityTypeConfiguration<GroupCampaignAssignment>
{
    public void Configure(EntityTypeBuilder<GroupCampaignAssignment> builder)
    {
        builder.ToTable("group_campaign_assignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(a => a.CampaignId).HasColumnName("campaign_id").IsRequired();
        builder.Property(a => a.Proficiency).HasColumnName("proficiency");
        builder.Property(a => a.IsActive).HasColumnName("is_active");
        builder.Property(a => a.AssignedAt).HasColumnName("assigned_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(a => a.Group)
               .WithMany(g => g.CampaignAssignments)
               .HasForeignKey(a => a.GroupId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.GroupId, a.CampaignId }).IsUnique();
        builder.HasIndex(a => new { a.CampaignId, a.IsActive, a.Proficiency });
    }
}
