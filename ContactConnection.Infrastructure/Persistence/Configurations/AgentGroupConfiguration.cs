using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class AgentGroupConfiguration : IEntityTypeConfiguration<AgentGroup>
{
    public void Configure(EntityTypeBuilder<AgentGroup> builder)
    {
        builder.ToTable("agent_groups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Id).HasColumnName("id");
        builder.Property(g => g.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(g => g.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(g => g.Slug).HasColumnName("slug").HasMaxLength(100).IsRequired();
        builder.Property(g => g.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(g => g.IsActive).HasColumnName("is_active");
        builder.Property(g => g.CreatedAt).HasColumnName("created_at");
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at");

        builder.HasMany(g => g.Members)
               .WithOne()
               .HasForeignKey(m => m.GroupId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(g => g.CampaignAssignments)
               .WithOne(a => a.Group)
               .HasForeignKey(a => a.GroupId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(g => new { g.TenantId, g.Slug }).IsUnique();
    }
}
