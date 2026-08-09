using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class TenantApiPreferenceConfiguration : IEntityTypeConfiguration<TenantApiPreference>
{
    public void Configure(EntityTypeBuilder<TenantApiPreference> builder)
    {
        builder.ToTable("tenant_api_preferences");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.ApiSubType).HasColumnName("api_sub_type").HasMaxLength(64).IsRequired();
        builder.Property(p => p.Source).HasColumnName("source").HasMaxLength(16).IsRequired();
        builder.Property(p => p.EndpointId).HasColumnName("endpoint_id").IsRequired();
        builder.Property(p => p.SettingsJson).HasColumnName("settings_json").HasColumnType("jsonb");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        // One preference per sub-type per tenant (tenant scoping handled by search_path)
        builder.HasIndex(p => p.ApiSubType).IsUnique().HasDatabaseName("ix_tenant_api_preferences_api_sub_type");
    }
}
