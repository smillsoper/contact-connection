using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class BlockListConfiguration : IEntityTypeConfiguration<BlockListEntry>
{
    public void Configure(EntityTypeBuilder<BlockListEntry> builder)
    {
        builder.ToTable("block_list_entries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.TenantId).HasColumnName("tenant_id");
        builder.Property(e => e.PhoneNumber).HasColumnName("phone_number").IsRequired().HasMaxLength(30);
        builder.Property(e => e.MatchType).HasColumnName("match_type").IsRequired().HasMaxLength(20);
        builder.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => new { e.TenantId, e.PhoneNumber, e.MatchType })
            .HasDatabaseName("idx_block_list_tenant_number");

        builder.HasIndex(e => new { e.TenantId, e.ExpiresAt })
            .HasDatabaseName("idx_block_list_tenant_expiry");
    }
}
