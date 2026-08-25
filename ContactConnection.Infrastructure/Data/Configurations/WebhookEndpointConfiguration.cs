using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.TenantApiEndpointId).HasColumnName("tenant_api_endpoint_id").IsRequired();
        builder.Property(e => e.Token).HasColumnName("token").HasMaxLength(64).IsRequired();
        builder.Property(e => e.SignatureHeaderName).HasColumnName("signature_header_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.SignatureAlgorithm).HasColumnName("signature_algorithm").HasMaxLength(20).IsRequired();
        builder.Property(e => e.IncludeTimestamp).HasColumnName("include_timestamp").IsRequired();
        builder.Property(e => e.TimestampToleranceSeconds).HasColumnName("timestamp_tolerance_seconds").IsRequired();
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(e => e.CredentialKeyName);

        builder.HasIndex(e => e.TenantApiEndpointId).IsUnique().HasDatabaseName("ix_webhook_endpoints_tenant_api_endpoint_id");
        builder.HasIndex(e => e.Token).IsUnique().HasDatabaseName("ix_webhook_endpoints_token");
    }
}
