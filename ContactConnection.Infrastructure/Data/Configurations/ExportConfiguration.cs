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
    }
}
