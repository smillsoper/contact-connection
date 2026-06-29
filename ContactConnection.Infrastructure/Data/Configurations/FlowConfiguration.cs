using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class FlowConfiguration : IEntityTypeConfiguration<Flow>
{
    public void Configure(EntityTypeBuilder<Flow> builder)
    {
        builder.ToTable("flows");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .HasColumnName("id");

        builder.Property(f => f.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(f => f.ClientId)
            .HasColumnName("client_id");

        builder.Property(f => f.CampaignId)
            .HasColumnName("campaign_id");

        builder.Property(f => f.CreatedByAgentId)
            .HasColumnName("created_by_agent_id")
            .IsRequired();

        builder.Property(f => f.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(f => f.FlowType)
            .HasColumnName("flow_type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(f => f.FlowDirection)
            .HasColumnName("flow_direction")
            .HasMaxLength(20);

        builder.Property(f => f.FlowSubType)
            .HasColumnName("flow_sub_type")
            .HasMaxLength(20);

        builder.Property(f => f.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(f => f.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        // Flow definition JSON — stored as text, deserialized at engine load time
        builder.Property(f => f.Definition)
            .HasColumnName("definition")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(f => f.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Indexes
        builder.HasIndex(f => new { f.TenantId, f.IsActive })
            .HasDatabaseName("idx_flows_tenant_active");

        builder.HasIndex(f => new { f.TenantId, f.ClientId, f.CampaignId })
            .HasDatabaseName("idx_flows_tenant_client_campaign");
    }
}
