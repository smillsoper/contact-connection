using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class CoachingNoteConfiguration : IEntityTypeConfiguration<CoachingNote>
{
    public void Configure(EntityTypeBuilder<CoachingNote> b)
    {
        b.ToTable("coaching_notes");
        b.HasKey(n => n.Id);
        b.Property(n => n.Id).HasColumnName("id");
        b.Property(n => n.TenantId).HasColumnName("tenant_id");
        b.Property(n => n.AgentId).HasColumnName("agent_id");
        b.Property(n => n.FromId).HasColumnName("from_id");
        b.Property(n => n.FromName).HasColumnName("from_name").HasMaxLength(200).IsRequired();
        b.Property(n => n.Text).HasColumnName("text").HasMaxLength(CoachingNote.MaxLength).IsRequired();
        b.Property(n => n.CallRecordId).HasColumnName("call_record_id");
        b.Property(n => n.CreatedAt).HasColumnName("created_at");
        b.Property(n => n.SeenAt).HasColumnName("seen_at");
        b.Property(n => n.AcknowledgedAt).HasColumnName("acknowledged_at");
        b.Property(n => n.RetractedAt).HasColumnName("retracted_at");
        b.Ignore(n => n.IsOpen);
        b.Ignore(n => n.Status);
        b.HasIndex(n => new { n.AgentId, n.CreatedAt }).HasDatabaseName("ix_coaching_notes_agent_created");
        b.HasIndex(n => n.CallRecordId).HasDatabaseName("ix_coaching_notes_call");
    }
}
