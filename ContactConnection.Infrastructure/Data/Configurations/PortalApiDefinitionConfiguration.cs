using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class PortalApiDefinitionConfiguration : IEntityTypeConfiguration<PortalApiDefinition>
{
    public void Configure(EntityTypeBuilder<PortalApiDefinition> builder)
    {
        builder.ToTable("portal_api_definitions");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.ApiType).HasColumnName("api_type").HasMaxLength(64).IsRequired();
        builder.Property(d => d.Provider).HasColumnName("provider").HasMaxLength(100);
        builder.Property(d => d.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(d => d.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(d => d.HttpMethod).HasColumnName("http_method").HasMaxLength(10).IsRequired();
        builder.Property(d => d.BaseUrl).HasColumnName("base_url").HasMaxLength(2000).IsRequired();
        builder.Property(d => d.TimeoutSeconds).HasColumnName("timeout_seconds").IsRequired().HasDefaultValue(30);
        builder.Property(d => d.Headers).HasColumnName("headers").HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.QueryParams).HasColumnName("query_params").HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.RequestBodyTemplate).HasColumnName("request_body_template");
        builder.Property(d => d.ResponseMapping).HasColumnName("response_mapping").HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.AuthConfig).HasColumnName("auth_config").HasColumnType("jsonb").IsRequired();
        builder.Property(d => d.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(d => d.ApiType).HasDatabaseName("ix_portal_api_definitions_api_type");
        builder.HasIndex(d => new { d.ApiType, d.IsActive }).HasDatabaseName("ix_portal_api_definitions_api_type_active");
    }
}
