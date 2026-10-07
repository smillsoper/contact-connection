using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

/// <summary>Agent dedications (S183) — campaign ids as a native uuid[], weekly windows as jsonb text.</summary>
public class AgentDedicationConfiguration : IEntityTypeConfiguration<AgentDedication>
{
    public void Configure(EntityTypeBuilder<AgentDedication> b)
    {
        b.ToTable("agent_dedications");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).HasColumnName("id");
        b.Property(d => d.TenantId).HasColumnName("tenant_id");
        b.Property(d => d.AgentId).HasColumnName("agent_id");
        b.Property(d => d.CampaignIds).HasColumnName("campaign_ids").HasColumnType("uuid[]");
        b.Property(d => d.Mode).HasColumnName("mode").HasMaxLength(12).IsRequired();
        b.Property(d => d.StartsAt).HasColumnName("starts_at");
        b.Property(d => d.EndsAt).HasColumnName("ends_at");
        b.Property(d => d.WindowsJson).HasColumnName("windows").HasColumnType("jsonb");
        b.Property(d => d.TimeZone).HasColumnName("time_zone").HasMaxLength(64).IsRequired();
        b.Property(d => d.Note).HasColumnName("note").HasMaxLength(200);
        b.Property(d => d.CreatedById).HasColumnName("created_by_id");
        b.Property(d => d.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200).IsRequired();
        b.Property(d => d.CreatedAt).HasColumnName("created_at");
        b.Property(d => d.EndedAt).HasColumnName("ended_at");
        b.Property(d => d.EndedById).HasColumnName("ended_by_id");
        b.Ignore(d => d.Windows);
        b.HasIndex(d => new { d.AgentId, d.EndedAt }).HasDatabaseName("ix_agent_dedications_agent_ended");
    }
}
