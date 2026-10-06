using System.Text.Json;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

/// <summary>Export Worker (S180): the spec is one JSONB document, compared by its JSON so a replaced spec always saves.</summary>
internal static class ExportSpecMapping
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<ExportSpec> MapSpec(this PropertyBuilder<ExportSpec> p) => p
        .HasColumnName("spec")
        .HasColumnType("jsonb")
        .HasConversion(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<ExportSpec>(v, JsonOptions) ?? new ExportSpec(),
            new ValueComparer<ExportSpec>(
                (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                v => JsonSerializer.Deserialize<ExportSpec>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!))
        .IsRequired();

    /// <summary>Any JSONB value object, compared by its JSON (EF can't see in-place changes to a list otherwise).</summary>
    public static PropertyBuilder<T> MapJson<T>(this PropertyBuilder<T> p, string column, Func<T> empty) => p
        .HasColumnName(column)
        .HasColumnType("jsonb")
        .HasConversion(
            v => v == null ? null! : JsonSerializer.Serialize(v, JsonOptions),
            v => v == null ? default! : JsonSerializer.Deserialize<T>(v, JsonOptions) ?? empty(),
            new ValueComparer<T>(
                (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                v => v == null ? default! : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!));
}

public class ExportDefinitionConfiguration : IEntityTypeConfiguration<ExportDefinition>
{
    public void Configure(EntityTypeBuilder<ExportDefinition> b)
    {
        b.ToTable("export_definitions");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).HasColumnName("id");
        b.Property(d => d.TenantId).HasColumnName("tenant_id");
        b.Property(d => d.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(d => d.Description).HasColumnName("description").HasMaxLength(1000);
        b.Property(d => d.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(d => d.Spec).MapSpec();
        b.Property(d => d.SpecRevision).HasColumnName("spec_revision");
        b.Property(d => d.Schedule).MapJson<ExportSchedule?>("schedule", () => null);
        b.Property(d => d.DeliveryTargets).MapJson("delivery_targets", () => new List<ExportDeliveryTarget>())
            .HasDefaultValueSql("'[]'::jsonb").IsRequired();
        b.Property(d => d.LastScheduledFor).HasColumnName("last_scheduled_for");
        b.Property(d => d.ApprovedAt).HasColumnName("approved_at");
        b.Property(d => d.ApprovedByVendorContact).HasColumnName("approved_by_vendor_contact").HasMaxLength(200);
        b.Property(d => d.ApprovalRecordedByName).HasColumnName("approval_recorded_by_name").HasMaxLength(200);
        b.Property(d => d.ApprovalNote).HasColumnName("approval_note").HasMaxLength(2000);
        b.Property(d => d.ApprovedRunId).HasColumnName("approved_run_id");
        b.Property(d => d.ApprovedSpecRevision).HasColumnName("approved_spec_revision");
        b.Property(d => d.CreatedAt).HasColumnName("created_at");
        b.Property(d => d.UpdatedAt).HasColumnName("updated_at");
        b.Ignore(d => d.ChangedSinceApproval);
        b.HasIndex(d => d.Name).HasDatabaseName("idx_export_definitions_name");
    }
}

public class ExportRunConfiguration : IEntityTypeConfiguration<ExportRun>
{
    public void Configure(EntityTypeBuilder<ExportRun> b)
    {
        b.ToTable("export_runs");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.TenantId).HasColumnName("tenant_id");
        b.Property(r => r.DefinitionId).HasColumnName("definition_id");
        b.Property(r => r.DefinitionName).HasColumnName("definition_name").HasMaxLength(200).IsRequired();
        b.Property(r => r.SpecRevision).HasColumnName("spec_revision");
        b.Property(r => r.Spec).MapSpec();
        b.Property(r => r.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
        b.Property(r => r.IsTest).HasColumnName("is_test");
        b.Property(r => r.DataSource).HasColumnName("data_source").HasMaxLength(20).IsRequired();
        b.Property(r => r.WindowStart).HasColumnName("window_start");
        b.Property(r => r.WindowEnd).HasColumnName("window_end");
        b.Property(r => r.ScheduledFor).HasColumnName("scheduled_for");
        b.Property(r => r.Deliver).HasColumnName("deliver");
        b.Property(r => r.FileDeletedAt).HasColumnName("file_deleted_at");
        b.Property(r => r.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(r => r.Attempts).HasColumnName("attempts");
        b.Property(r => r.MaxAttempts).HasColumnName("max_attempts");
        b.Property(r => r.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(r => r.Error).HasColumnName("error");
        b.Property(r => r.RequestedById).HasColumnName("requested_by_id");
        b.Property(r => r.RequestedByName).HasColumnName("requested_by_name").HasMaxLength(200);
        b.Property(r => r.RowCount).HasColumnName("row_count");
        b.Property(r => r.CallCount).HasColumnName("call_count");
        b.Property(r => r.FileName).HasColumnName("file_name").HasMaxLength(300);
        b.Property(r => r.BlobKey).HasColumnName("blob_key").HasMaxLength(400);
        b.Property(r => r.ContentType).HasColumnName("content_type").HasMaxLength(100);
        b.Property(r => r.FileSize).HasColumnName("file_size");
        b.Property(r => r.Sha256).HasColumnName("sha256").HasMaxLength(64);
        b.Property(r => r.QueuedAt).HasColumnName("queued_at");
        b.Property(r => r.StartedAt).HasColumnName("started_at");
        b.Property(r => r.FinishedAt).HasColumnName("finished_at");
        b.HasIndex(r => new { r.DefinitionId, r.QueuedAt }).HasDatabaseName("idx_export_runs_definition");
        b.HasIndex(r => new { r.Status, r.NextAttemptAt }).HasDatabaseName("idx_export_runs_queue");
        // The scheduler can never queue the same scheduled run twice (two Workers, or a retry after a crash).
        b.HasIndex(r => new { r.DefinitionId, r.ScheduledFor }).IsUnique()
            .HasFilter("scheduled_for IS NOT NULL").HasDatabaseName("ux_export_runs_scheduled");
    }
}

public class ExportDeliveryConfiguration : IEntityTypeConfiguration<ExportDelivery>
{
    public void Configure(EntityTypeBuilder<ExportDelivery> b)
    {
        b.ToTable("export_deliveries");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).HasColumnName("id");
        b.Property(d => d.TenantId).HasColumnName("tenant_id");
        b.Property(d => d.RunId).HasColumnName("run_id");
        b.Property(d => d.DefinitionId).HasColumnName("definition_id");
        b.Property(d => d.TargetId).HasColumnName("target_id");
        b.Property(d => d.TargetName).HasColumnName("target_name").HasMaxLength(200).IsRequired();
        b.Property(d => d.TargetType).HasColumnName("target_type").HasMaxLength(20).IsRequired();
        b.Property(d => d.IsTest).HasColumnName("is_test");
        b.Property(d => d.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(d => d.Attempts).HasColumnName("attempts");
        b.Property(d => d.MaxAttempts).HasColumnName("max_attempts");
        b.Property(d => d.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(d => d.Error).HasColumnName("error");
        b.Property(d => d.SentAs).HasColumnName("sent_as").HasMaxLength(1000);
        b.Property(d => d.RequestedByName).HasColumnName("requested_by_name").HasMaxLength(200);
        b.Property(d => d.QueuedAt).HasColumnName("queued_at");
        b.Property(d => d.StartedAt).HasColumnName("started_at");
        b.Property(d => d.DeliveredAt).HasColumnName("delivered_at");
        b.HasIndex(d => d.RunId).HasDatabaseName("idx_export_deliveries_run");
        b.HasIndex(d => new { d.Status, d.NextAttemptAt }).HasDatabaseName("idx_export_deliveries_queue");
    }
}

public class ExportAuditEntryConfiguration : IEntityTypeConfiguration<ExportAuditEntry>
{
    public void Configure(EntityTypeBuilder<ExportAuditEntry> b)
    {
        b.ToTable("export_audit_entries");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.TenantId).HasColumnName("tenant_id");
        b.Property(e => e.DefinitionId).HasColumnName("definition_id");
        b.Property(e => e.RunId).HasColumnName("run_id");
        b.Property(e => e.Action).HasColumnName("action").HasMaxLength(40).IsRequired();
        b.Property(e => e.ActorName).HasColumnName("actor_name").HasMaxLength(200);
        b.Property(e => e.Detail).HasColumnName("detail").HasMaxLength(1000);
        b.Property(e => e.At).HasColumnName("at");
        b.HasIndex(e => new { e.DefinitionId, e.At }).HasDatabaseName("idx_export_audit_definition");
    }
}
