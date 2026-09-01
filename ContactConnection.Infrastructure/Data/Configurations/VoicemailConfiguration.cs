using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class VoicemailConfiguration : IEntityTypeConfiguration<Voicemail>
{
    public void Configure(EntityTypeBuilder<Voicemail> builder)
    {
        builder.ToTable("voicemails");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id).HasColumnName("id");
        builder.Property(v => v.TenantId).HasColumnName("tenant_id");
        builder.Property(v => v.CallRecordId).HasColumnName("call_record_id");
        builder.Property(v => v.CampaignId).HasColumnName("campaign_id");
        builder.Property(v => v.CallerId).HasColumnName("caller_id").HasMaxLength(30);

        builder.Property(v => v.StorageKey).HasColumnName("storage_key").HasMaxLength(300).IsRequired();
        builder.Property(v => v.DurationSeconds).HasColumnName("duration_seconds");
        builder.Property(v => v.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(v => v.Transcription).HasColumnName("transcription");

        builder.Property(v => v.EmailDeliveryStatus).HasColumnName("email_delivery_status").HasMaxLength(20);
        builder.Property(v => v.EmailDeliveredTo).HasColumnName("email_delivered_to").HasMaxLength(2000);
        builder.Property(v => v.EmailDeliveryError).HasColumnName("email_delivery_error").HasMaxLength(1000);
        builder.Property(v => v.EmailDeliveredAt).HasColumnName("email_delivered_at");

        builder.Property(v => v.CreatedAt).HasColumnName("created_at");
        builder.Property(v => v.HeardAt).HasColumnName("heard_at");
        builder.Property(v => v.HeardBy).HasColumnName("heard_by");
        builder.Property(v => v.ArchivedAt).HasColumnName("archived_at");

        builder.HasIndex(v => v.CallRecordId).HasDatabaseName("idx_voicemails_call_record");
        builder.HasIndex(v => new { v.CampaignId, v.Status }).HasDatabaseName("idx_voicemails_campaign_status");
    }
}
