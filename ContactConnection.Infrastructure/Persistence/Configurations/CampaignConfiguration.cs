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
        builder.Property(c => c.InboundFlowId).HasColumnName("inbound_flow_id");
        builder.Property(c => c.OutboundFlowId).HasColumnName("outbound_flow_id");
        builder.Property(c => c.Direction).HasColumnName("direction").HasMaxLength(20).IsRequired();
        builder.Property(c => c.DialMode).HasColumnName("dial_mode").HasMaxLength(20).IsRequired();
        builder.Property(c => c.CallerIdNumber).HasColumnName("caller_id_number").HasMaxLength(30);
        builder.Property(c => c.Priority).HasColumnName("priority");
        builder.Property(c => c.RingStrategy).HasColumnName("ring_strategy").HasMaxLength(30).IsRequired();
        builder.Property(c => c.RingTopN).HasColumnName("ring_top_n");
        builder.Property(c => c.AfterCallWorkSeconds).HasColumnName("after_call_work_seconds");
        builder.Property(c => c.MaxQueueSize).HasColumnName("max_queue_size");
        builder.Property(c => c.QueueTimeoutSeconds).HasColumnName("queue_timeout_seconds");
        builder.Property(c => c.ServiceLevelThresholdSeconds).HasColumnName("service_level_threshold_seconds");
        builder.Property(c => c.ShortAbandonThresholdSeconds).HasColumnName("short_abandon_threshold_seconds");
        builder.Property(c => c.QueueAccelerationEnabled).HasColumnName("queue_acceleration_enabled");
        builder.Property(c => c.QueueAccelerationIntervalSeconds).HasColumnName("queue_acceleration_interval_seconds");
        builder.Property(c => c.QueueAccelerationPriorityBoost).HasColumnName("queue_acceleration_priority_boost");
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
