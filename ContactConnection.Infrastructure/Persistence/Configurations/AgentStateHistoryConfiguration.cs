using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class AgentStateHistoryConfiguration : IEntityTypeConfiguration<AgentStateHistoryEntry>
{
    public void Configure(EntityTypeBuilder<AgentStateHistoryEntry> b)
    {
        b.ToTable("agent_state_history");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        b.Property(x => x.StateCode).HasColumnName("state_code").HasMaxLength(30).IsRequired();
        b.Property(x => x.Label).HasColumnName("label").HasMaxLength(200).IsRequired();
        b.Property(x => x.CustomCodeId).HasColumnName("custom_code_id");
        b.Property(x => x.EnteredAt).HasColumnName("entered_at").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex(x => new { x.AgentId, x.EnteredAt });
        b.HasIndex(x => new { x.TenantId, x.EnteredAt });
    }
}
