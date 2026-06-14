using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("campaigns");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(c => c.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(c => c.Slug).HasColumnName("slug").HasMaxLength(100).IsRequired();
        builder.Property(c => c.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(c => c.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(c => c.FlowId).HasColumnName("flow_id");
        builder.Property(c => c.MaxQueueSize).HasColumnName("max_queue_size");
        builder.Property(c => c.QueueTimeoutSeconds).HasColumnName("queue_timeout_seconds");
        builder.Property(c => c.ServiceLevelThresholdSeconds).HasColumnName("service_level_threshold_seconds");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(c => c.Client)
               .WithMany(cl => cl.Campaigns)
               .HasForeignKey(c => c.ClientId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.PhoneNumbers)
               .WithOne(p => p.Campaign)
               .HasForeignKey(p => p.CampaignId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.AgentAssignments)
               .WithOne()
               .HasForeignKey(a => a.CampaignId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.GroupAssignments)
               .WithOne()
               .HasForeignKey(a => a.CampaignId)
               .OnDelete(DeleteBehavior.Cascade);

        // Slug unique per tenant (search_path scopes this to the tenant schema)
        builder.HasIndex(c => new { c.TenantId, c.Slug }).IsUnique();
        builder.HasIndex(c => c.ClientId);
        builder.HasIndex(c => c.Status);
    }
}
