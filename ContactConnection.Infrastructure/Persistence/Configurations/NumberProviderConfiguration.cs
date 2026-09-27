using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class NumberProviderConfiguration : IEntityTypeConfiguration<NumberProvider>
{
    public void Configure(EntityTypeBuilder<NumberProvider> builder)
    {
        builder.ToTable("number_providers");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Type).HasColumnName("type").HasMaxLength(30).IsRequired();
        builder.Property(p => p.SipGatewayId).HasColumnName("sip_gateway_id");
        builder.Property(p => p.SourceIps).HasColumnName("source_ips").HasMaxLength(500);
        builder.Property(p => p.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(p => p.IsActive).HasColumnName("is_active");
        builder.Property(p => p.ApiKeyHash).HasColumnName("api_key_hash").HasMaxLength(64);
        builder.Property(p => p.ApiKeyPrefix).HasColumnName("api_key_prefix").HasMaxLength(16);
        builder.Property(p => p.ApiKeyIssuedAt).HasColumnName("api_key_issued_at");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(p => p.Name).IsUnique();
        // External routing requests authenticate by key hash.
        builder.HasIndex(p => p.ApiKeyHash).IsUnique().HasFilter("api_key_hash IS NOT NULL");
    }
}
