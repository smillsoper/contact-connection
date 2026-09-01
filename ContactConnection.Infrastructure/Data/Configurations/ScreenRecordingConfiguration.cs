using System.Text.Json;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class ScreenRecordingConfiguration : IEntityTypeConfiguration<ScreenRecording>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // JSONB list properties are mutated in place (list.Add(...)) — without an explicit comparer
    // EF's change detection compares by reference and misses the mutation, so the column never
    // gets written. Snapshot with a shallow copy; equate by element sequence.
    private static ValueComparer<List<T>> ListComparer<T>() => new(
        (a, b) => (a ?? new List<T>()).SequenceEqual(b ?? new List<T>()),
        v => v == null ? 0 : v.Aggregate(0, (h, x) => HashCode.Combine(h, x!.GetHashCode())),
        v => v == null ? new List<T>() : v.ToList());

    public void Configure(EntityTypeBuilder<ScreenRecording> builder)
    {
        builder.ToTable("screen_recordings");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.TenantId).HasColumnName("tenant_id");
        builder.Property(r => r.CallRecordId).HasColumnName("call_record_id");
        builder.Property(r => r.InteractionId).HasColumnName("interaction_id");
        builder.Property(r => r.AgentId).HasColumnName("agent_id");

        builder.Property(r => r.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(r => r.Container).HasColumnName("container").HasMaxLength(10).IsRequired();
        builder.Property(r => r.Codec).HasColumnName("codec").HasMaxLength(100);

        builder.Property(r => r.StartedAtServer).HasColumnName("started_at_server");
        builder.Property(r => r.StartedAtClient).HasColumnName("started_at_client");
        builder.Property(r => r.ClientClockOffsetMs).HasColumnName("client_clock_offset_ms");

        builder.Property(r => r.StorageKey).HasColumnName("storage_key").HasMaxLength(300).IsRequired();
        builder.Property(r => r.TotalBytes).HasColumnName("total_bytes");
        builder.Property(r => r.DurationMs).HasColumnName("duration_ms");
        builder.Property(r => r.Sha256).HasColumnName("sha256").HasMaxLength(64);
        builder.Property(r => r.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);

        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at");

        builder.Ignore(r => r.ChunkCount);   // derived from ReceivedChunkIndices

        builder.Property(r => r.ReceivedChunkIndices)
            .HasColumnName("received_chunk_indices")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<int>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb")
            .Metadata.SetValueComparer(ListComparer<int>());

        builder.Property(r => r.CuePoints)
            .HasColumnName("cue_points")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<ScreenRecordingCuePoint>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb")
            .Metadata.SetValueComparer(ListComparer<ScreenRecordingCuePoint>());

        builder.HasIndex(r => r.CallRecordId).HasDatabaseName("idx_screen_recordings_call_record");
        builder.HasIndex(r => new { r.TenantId, r.Status }).HasDatabaseName("idx_screen_recordings_tenant_status");
    }
}
