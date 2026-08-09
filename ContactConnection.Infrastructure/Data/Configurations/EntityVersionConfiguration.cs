using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

/// <summary>Shared between TenantDbContext (flow/tenant_api_definition/tenant_api_endpoint rows)
/// and ContactConnectionDbContext (portal_api_definition/portal_api_endpoint rows) — same table
/// shape in both schemas, applied explicitly in each context's OnModelCreating.</summary>
public class EntityVersionConfiguration : IEntityTypeConfiguration<EntityVersion>
{
    public void Configure(EntityTypeBuilder<EntityVersion> builder)
    {
        builder.ToTable("entity_versions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id");
        builder.Property(v => v.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(v => v.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(v => v.VersionNumber).HasColumnName("version_number").IsRequired();
        builder.Property(v => v.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(v => v.CreatedById).HasColumnName("created_by_id").IsRequired();
        builder.Property(v => v.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();
        builder.Property(v => v.ChangeSummary).HasColumnName("change_summary").HasMaxLength(500);
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").IsRequired();

        // The set of versions for one entity is always looked up by (EntityType, EntityId), and
        // a partial unique index enforces "exactly one active version per entity" at the
        // database level, not just in application code.
        builder.HasIndex(v => new { v.EntityType, v.EntityId })
            .HasDatabaseName("ix_entity_versions_entity");
        builder.HasIndex(v => new { v.EntityType, v.EntityId, v.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ix_entity_versions_entity_version_number");
        builder.HasIndex(v => new { v.EntityType, v.EntityId, v.IsActive })
            .IsUnique()
            .HasFilter("is_active")
            .HasDatabaseName("ix_entity_versions_entity_active");
    }
}
