using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class CallStateHistoryConfiguration : IEntityTypeConfiguration<CallStateHistoryEntry>
{
    public void Configure(EntityTypeBuilder<CallStateHistoryEntry> b)
    {
        b.ToTable("call_state_history");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.CallRecordId).HasColumnName("call_record_id").IsRequired();
        b.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        b.Property(x => x.State).HasColumnName("state").HasMaxLength(20).IsRequired();
        b.Property(x => x.CampaignId).HasColumnName("campaign_id").IsRequired();
        b.Property(x => x.AgentId).HasColumnName("agent_id");
        b.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(500);
        b.Property(x => x.AbandonType).HasColumnName("abandon_type").HasMaxLength(30);
        b.Property(x => x.AbandonLength).HasColumnName("abandon_length").HasMaxLength(10);
        b.Property(x => x.MetServiceLevel).HasColumnName("met_service_level");
        b.Property(x => x.EnteredAt).HasColumnName("entered_at").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex(x => new { x.CallRecordId, x.Sequence });
        b.HasIndex(x => new { x.TenantId, x.CampaignId, x.EnteredAt });
    }
}
