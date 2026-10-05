using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class CallSummaryConfiguration : IEntityTypeConfiguration<CallSummary>
{
    public void Configure(EntityTypeBuilder<CallSummary> b)
    {
        b.ToTable("call_summaries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.CallRecordId).HasColumnName("call_record_id");
        b.Property(x => x.InteractionId).HasColumnName("interaction_id");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(x => x.AiSummary).HasColumnName("ai_summary").IsRequired();
        b.Property(x => x.AiReasonForCall).HasColumnName("ai_reason_for_call").HasMaxLength(500);
        b.Property(x => x.AiOutcome).HasColumnName("ai_outcome").HasMaxLength(30);
        b.Property(x => x.AiDisposition).HasColumnName("ai_disposition").HasMaxLength(300);
        b.Property(x => x.AiDispositionValid).HasColumnName("ai_disposition_valid");
        b.Property(x => x.AiConfidence).HasColumnName("ai_confidence");
        b.Property(x => x.AiFollowUp).HasColumnName("ai_follow_up").HasMaxLength(1000);
        b.Property(x => x.AiIsTestCall).HasColumnName("ai_is_test_call");
        b.Property(x => x.PossibleTestCall).HasColumnName("possible_test_call");
        b.Property(x => x.Model).HasColumnName("model").HasMaxLength(100);
        b.Property(x => x.InputTokens).HasColumnName("input_tokens");
        b.Property(x => x.OutputTokens).HasColumnName("output_tokens");
        b.Property(x => x.CostUsd).HasColumnName("cost_usd").HasPrecision(12, 6);
        b.Property(x => x.ElapsedMs).HasColumnName("elapsed_ms");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.CreatedByName).HasColumnName("created_by_name").HasMaxLength(200);
        b.Property(x => x.Summary).HasColumnName("summary");
        b.Property(x => x.ReasonForCall).HasColumnName("reason_for_call").HasMaxLength(500);
        b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(30);
        b.Property(x => x.Disposition).HasColumnName("disposition").HasMaxLength(300);
        b.Property(x => x.FollowUp).HasColumnName("follow_up").HasMaxLength(1000);
        b.Property(x => x.Edited).HasColumnName("edited");
        b.Property(x => x.ReviewedById).HasColumnName("reviewed_by_id");
        b.Property(x => x.ReviewedByName).HasColumnName("reviewed_by_name").HasMaxLength(200);
        b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        b.HasOne<CallRecord>().WithMany().HasForeignKey(x => x.CallRecordId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.CallRecordId, x.CreatedAt }).HasDatabaseName("ix_call_summaries_call");
        b.HasIndex(x => x.Status).HasDatabaseName("ix_call_summaries_status");
    }
}
