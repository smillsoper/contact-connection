using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

/// <summary>Shared between TenantDbContext (tenant credential audit rows) and
/// ContactConnectionDbContext (portal credential audit rows) — same table shape in both schemas,
/// applied explicitly in each context's OnModelCreating. See EntityVersionConfiguration for the
/// analogous split on the version-history side.</summary>
public class CredentialAuditEntryConfiguration : IEntityTypeConfiguration<CredentialAuditEntry>
{
    public void Configure(EntityTypeBuilder<CredentialAuditEntry> builder)
    {
        builder.ToTable("credential_audit_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.KeyName).HasColumnName("key_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Action).HasColumnName("action").HasMaxLength(20).IsRequired();
        builder.Property(e => e.ActorId).HasColumnName("actor_id").IsRequired();
        builder.Property(e => e.ActorName).HasColumnName("actor_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        // History for one key is always looked up by KeyName, newest first.
        builder.HasIndex(e => new { e.KeyName, e.CreatedAt })
            .HasDatabaseName("ix_credential_audit_entries_key_name_created_at");
    }
}
