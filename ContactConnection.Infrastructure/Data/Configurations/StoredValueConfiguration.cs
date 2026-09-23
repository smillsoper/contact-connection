using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class StoredValueConfiguration : IEntityTypeConfiguration<StoredValue>
{
    public void Configure(EntityTypeBuilder<StoredValue> builder)
    {
        builder.ToTable("stored_values");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id).HasColumnName("id");
        builder.Property(v => v.TenantId).HasColumnName("tenant_id");
        builder.Property(v => v.Scope).HasColumnName("scope").IsRequired().HasMaxLength(20);
        builder.Property(v => v.ScopeId).HasColumnName("scope_id");
        builder.Property(v => v.KeyName).HasColumnName("key_name").IsRequired().HasMaxLength(200);
        builder.Property(v => v.Value).HasColumnName("value").IsRequired();
        builder.Property(v => v.ExpiresAt).HasColumnName("expires_at");
        builder.Property(v => v.CreatedAt).HasColumnName("created_at");
        builder.Property(v => v.UpdatedAt).HasColumnName("updated_at");

        // scope_id uses Guid.Empty (not null) for tenant scope specifically so this unique index
        // works under normal SQL semantics — Postgres treats NULL <> NULL in a unique index, which
        // would let a race create duplicate "tenant scope, same key" rows if scope_id were nullable.
        builder.HasIndex(v => new { v.TenantId, v.Scope, v.ScopeId, v.KeyName })
            .IsUnique()
            .HasDatabaseName("ix_stored_values_tenant_scope_key_unique");

        builder.HasIndex(v => new { v.TenantId, v.ExpiresAt })
            .HasDatabaseName("idx_stored_values_tenant_expiry");
    }
}
