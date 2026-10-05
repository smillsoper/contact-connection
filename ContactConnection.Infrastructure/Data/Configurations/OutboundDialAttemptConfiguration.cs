using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class OutboundDialAttemptConfiguration : IEntityTypeConfiguration<OutboundDialAttempt>
{
    public void Configure(EntityTypeBuilder<OutboundDialAttempt> builder)
    {
        builder.ToTable("outbound_dial_attempts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.AgentId).HasColumnName("agent_id");
        builder.Property(a => a.CampaignId).HasColumnName("campaign_id");
        builder.Property(a => a.DialedNumber).HasColumnName("dialed_number").HasMaxLength(30).IsRequired();
        builder.Property(a => a.CallerId).HasColumnName("caller_id").HasMaxLength(30);
        builder.Property(a => a.Result).HasColumnName("result").HasMaxLength(20).IsRequired();
        builder.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(a => a.CalleeTimeZone).HasColumnName("callee_time_zone").HasMaxLength(60);
        builder.Property(a => a.TimeZoneSource).HasColumnName("time_zone_source").HasMaxLength(40);
        builder.Property(a => a.CallRecordId).HasColumnName("call_record_id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(a => a.CreatedAt).HasDatabaseName("ix_outbound_dial_attempts_created");
        builder.HasIndex(a => new { a.AgentId, a.CreatedAt }).HasDatabaseName("ix_outbound_dial_attempts_agent_created");
    }
}
