using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class CallRecordAuditEntryConfiguration : IEntityTypeConfiguration<CallRecordAuditEntry>
{
    public void Configure(EntityTypeBuilder<CallRecordAuditEntry> builder)
    {
        builder.ToTable("call_record_audit_entries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CallRecordId).HasColumnName("call_record_id");
        builder.Property(e => e.Action).HasColumnName("action").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();
        builder.Property(e => e.Detail).HasColumnName("detail").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ActorId).HasColumnName("actor_id");
        builder.Property(e => e.ActorName).HasColumnName("actor_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(e => new { e.CallRecordId, e.CreatedAt })
            .HasDatabaseName("ix_call_record_audit_entries_call_created");
    }
}
