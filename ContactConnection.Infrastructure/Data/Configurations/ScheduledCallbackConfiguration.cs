using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class ScheduledCallbackConfiguration : IEntityTypeConfiguration<ScheduledCallback>
{
    public void Configure(EntityTypeBuilder<ScheduledCallback> builder)
    {
        builder.ToTable("scheduled_callbacks");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.TenantId).HasColumnName("tenant_id");
        builder.Property(c => c.CallRecordId).HasColumnName("call_record_id");
        builder.Property(c => c.CampaignId).HasColumnName("campaign_id");
        builder.Property(c => c.CallbackNumber).HasColumnName("callback_number").HasMaxLength(30).IsRequired();
        builder.Property(c => c.Dnis).HasColumnName("dnis").HasMaxLength(30);
        builder.Property(c => c.CallerIdOverride).HasColumnName("caller_id_override").HasMaxLength(64);
        builder.Property(c => c.TargetFlowId).HasColumnName("target_flow_id");
        builder.Property(c => c.TargetCampaignId).HasColumnName("target_campaign_id");

        builder.Property(c => c.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        builder.Property(c => c.RequestedAt).HasColumnName("requested_at");
        builder.Property(c => c.ScheduledFor).HasColumnName("scheduled_for");
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");

        builder.Property(c => c.AttemptCount).HasColumnName("attempt_count");
        builder.Property(c => c.MaxAttempts).HasColumnName("max_attempts");
        builder.Property(c => c.LastAttemptAt).HasColumnName("last_attempt_at");

        builder.Property(c => c.OutboundCallRecordId).HasColumnName("outbound_call_record_id");

        builder.Property(c => c.CompletedAt).HasColumnName("completed_at");
        builder.Property(c => c.AbandonedAt).HasColumnName("abandoned_at");
        builder.Property(c => c.ExpiredAt).HasColumnName("expired_at");
        builder.Property(c => c.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(c => c.Detail).HasColumnName("detail").HasMaxLength(1000);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(c => c.CallRecordId).HasDatabaseName("idx_scheduled_callbacks_call_record");
        builder.HasIndex(c => new { c.CampaignId, c.Status }).HasDatabaseName("idx_scheduled_callbacks_campaign_status");

        // The worker's due-scan filters on status + scheduled_for across all campaigns.
        builder.HasIndex(c => new { c.Status, c.ScheduledFor }).HasDatabaseName("idx_scheduled_callbacks_status_scheduled");
    }
}
