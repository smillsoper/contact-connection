using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class CallTraceEventConfiguration : IEntityTypeConfiguration<CallTraceEvent>
{
    public void Configure(EntityTypeBuilder<CallTraceEvent> b)
    {
        b.ToTable("call_trace_events");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.CallRecordId).HasColumnName("call_record_id").IsRequired();
        b.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        b.Property(x => x.Engine).HasColumnName("engine").HasMaxLength(20).IsRequired();
        b.Property(x => x.NodeId).HasColumnName("node_id").HasMaxLength(200).IsRequired();
        b.Property(x => x.NodeType).HasColumnName("node_type").HasMaxLength(50).IsRequired();
        b.Property(x => x.Label).HasColumnName("label").HasMaxLength(200);
        b.Property(x => x.EnteredAt).HasColumnName("entered_at").IsRequired();
        b.Property(x => x.Detail).HasColumnName("detail");
        b.Property(x => x.TransitionTaken).HasColumnName("transition_taken").HasMaxLength(200);
        b.Property(x => x.ExitReason).HasColumnName("exit_reason").HasMaxLength(500);
        b.Property(x => x.NextNodeId).HasColumnName("next_node_id").HasMaxLength(200);
        b.Property(x => x.StateSnapshot).HasColumnName("state_snapshot").HasColumnType("jsonb");
        b.Property(x => x.CampaignId).HasColumnName("campaign_id");
        b.Property(x => x.FlowId).HasColumnName("flow_id");
        b.Property(x => x.Dnis).HasColumnName("dnis").HasMaxLength(32);
        b.Property(x => x.Ani).HasColumnName("ani").HasMaxLength(32);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex(x => new { x.CallRecordId, x.Sequence });
        b.HasIndex(x => new { x.TenantId, x.CampaignId });
        b.HasIndex(x => new { x.TenantId, x.FlowId });
        b.HasIndex(x => new { x.TenantId, x.Dnis });
    }
}
