using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class RecordingMergeJobConfiguration : IEntityTypeConfiguration<RecordingMergeJob>
{
    public void Configure(EntityTypeBuilder<RecordingMergeJob> builder)
    {
        builder.ToTable("recording_merge_jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id).HasColumnName("id");
        builder.Property(j => j.TenantId).HasColumnName("tenant_id");
        builder.Property(j => j.CallRecordId).HasColumnName("call_record_id");

        builder.Property(j => j.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(j => j.Attempts).HasColumnName("attempts").HasDefaultValue(0);
        builder.Property(j => j.MaxAttempts).HasColumnName("max_attempts").HasDefaultValue(5);
        builder.Property(j => j.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(j => j.LastError).HasColumnName("last_error");

        builder.Property(j => j.OutputBlobKey).HasColumnName("output_blob_key").HasMaxLength(400);
        builder.Property(j => j.OutputFormat).HasColumnName("output_format").HasMaxLength(10);
        builder.Property(j => j.OutputDurationMs).HasColumnName("output_duration_ms");
        builder.Property(j => j.HadVideo).HasColumnName("had_video").HasDefaultValue(false);
        builder.Property(j => j.ScreenRecordingId).HasColumnName("screen_recording_id");
        builder.Property(j => j.ScreenRecordingCount).HasColumnName("screen_recording_count").HasDefaultValue(0);
        builder.Property(j => j.FfmpegCommand).HasColumnName("ffmpeg_command");

        builder.Property(j => j.CreatedAt).HasColumnName("created_at");
        builder.Property(j => j.UpdatedAt).HasColumnName("updated_at");
        builder.Property(j => j.StartedAt).HasColumnName("started_at");
        builder.Property(j => j.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(j => j.CallRecordId)
            .IsUnique()
            .HasDatabaseName("idx_recording_merge_jobs_call_record");
        builder.HasIndex(j => new { j.Status, j.NextAttemptAt })
            .HasDatabaseName("idx_recording_merge_jobs_status_next");
    }
}
