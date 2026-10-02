using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class MediaAssignmentChangeConfiguration : IEntityTypeConfiguration<MediaAssignmentChange>
{
    public void Configure(EntityTypeBuilder<MediaAssignmentChange> b)
    {
        b.ToTable("media_assignment_changes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.PhoneNumberId).HasColumnName("phone_number_id");
        b.Property(x => x.AssignmentId).HasColumnName("assignment_id");
        b.Property(x => x.Action).HasColumnName("action").HasMaxLength(30).IsRequired();
        b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();
        b.Property(x => x.Before).HasColumnName("before").HasColumnType("jsonb");
        b.Property(x => x.After).HasColumnName("after").HasColumnType("jsonb");
        b.Property(x => x.ChangedBy).HasColumnName("changed_by").HasMaxLength(200);
        b.Property(x => x.ChangedAt).HasColumnName("changed_at");
        b.HasIndex(x => new { x.PhoneNumberId, x.ChangedAt }).HasDatabaseName("ix_media_assignment_changes_number");
    }
}

public class MediaReplayBatchConfiguration : IEntityTypeConfiguration<MediaReplayBatch>
{
    public void Configure(EntityTypeBuilder<MediaReplayBatch> b)
    {
        b.ToTable("media_replay_batches");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.PhoneNumberId).HasColumnName("phone_number_id");
        b.Property(x => x.From).HasColumnName("from_at");
        b.Property(x => x.To).HasColumnName("to_at");
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        b.Property(x => x.RequestedById).HasColumnName("requested_by_id");
        b.Property(x => x.RequestedBy).HasColumnName("requested_by").HasMaxLength(200);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(x => x.TotalCalls).HasColumnName("total_calls");
        b.Property(x => x.ProcessedCalls).HasColumnName("processed_calls");
        b.Property(x => x.ChangedCalls).HasColumnName("changed_calls");
        b.Property(x => x.Error).HasColumnName("error").HasMaxLength(2000);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.StartedAt).HasColumnName("started_at");
        b.Property(x => x.CompletedAt).HasColumnName("completed_at");
        b.HasIndex(x => x.Status).HasDatabaseName("ix_media_replay_batches_status");
    }
}
