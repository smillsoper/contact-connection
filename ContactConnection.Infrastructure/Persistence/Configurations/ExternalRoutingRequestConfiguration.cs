using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class ExternalRoutingRequestConfiguration : IEntityTypeConfiguration<ExternalRoutingRequest>
{
    public void Configure(EntityTypeBuilder<ExternalRoutingRequest> builder)
    {
        builder.ToTable("external_routing_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.TenantId).HasColumnName("tenant_id");
        builder.Property(r => r.NumberProviderId).HasColumnName("number_provider_id");
        builder.Property(r => r.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
        builder.Property(r => r.CampaignId).HasColumnName("campaign_id");
        builder.Property(r => r.AgentGroupId).HasColumnName("agent_group_id");
        builder.Property(r => r.ExternalSessionId).HasColumnName("external_session_id").HasMaxLength(200);
        builder.Property(r => r.Dnis).HasColumnName("dnis").HasMaxLength(50);
        builder.Property(r => r.Ani).HasColumnName("ani").HasMaxLength(50);
        builder.Property(r => r.Accepted).HasColumnName("accepted");
        builder.Property(r => r.Reason).HasColumnName("reason").HasMaxLength(200);
        builder.Property(r => r.Targets).HasColumnName("targets").HasMaxLength(500);
        builder.Property(r => r.ReportedCallDate).HasColumnName("reported_call_date").HasMaxLength(50);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        // Reporting: reject volume per campaign over time; reconciliation by provider.
        builder.HasIndex(r => new { r.CampaignId, r.CreatedAt });
        builder.HasIndex(r => new { r.NumberProviderId, r.Kind, r.CreatedAt });
    }
}
