using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class PortalApiEndpointConfiguration : IEntityTypeConfiguration<PortalApiEndpoint>
{
    public void Configure(EntityTypeBuilder<PortalApiEndpoint> builder)
    {
        builder.ToTable("portal_api_endpoints");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.DefinitionId).HasColumnName("definition_id").IsRequired();
        builder.Property(e => e.ApiSubType).HasColumnName("api_sub_type").HasMaxLength(64).IsRequired().HasDefaultValue("");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(e => e.Path).HasColumnName("path").HasMaxLength(2000).IsRequired();
        builder.Property(e => e.HttpMethod).HasColumnName("http_method").HasMaxLength(10);
        builder.Property(e => e.RequestBodyTemplate).HasColumnName("request_body_template");
        builder.Property(e => e.QueryParams).HasColumnName("query_params").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Headers).HasColumnName("headers").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ResponseMapping).HasColumnName("response_mapping").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired().HasDefaultValue(0);
        builder.Property(e => e.IsPreferred).HasColumnName("is_preferred").IsRequired().HasDefaultValue(false);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.DefinitionId).HasDatabaseName("ix_portal_api_endpoints_definition_id");
        builder.HasIndex(e => e.ApiSubType).HasDatabaseName("ix_portal_api_endpoints_api_sub_type");
    }
}
