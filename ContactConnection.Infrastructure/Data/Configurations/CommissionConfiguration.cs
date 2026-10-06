using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class CommissionRuleConfiguration : IEntityTypeConfiguration<CommissionRule>
{
    public void Configure(EntityTypeBuilder<CommissionRule> b)
    {
        b.ToTable("commission_rules");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.TenantId).HasColumnName("tenant_id");
        b.Property(r => r.ClientId).HasColumnName("client_id");
        b.Property(r => r.CampaignId).HasColumnName("campaign_id");
        b.Property(r => r.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(r => r.Kind).HasColumnName("kind").HasMaxLength(30).IsRequired();
        b.Property(r => r.Amount).HasColumnName("amount").HasPrecision(12, 4);
        b.Property(r => r.ProductId).HasColumnName("product_id");
        b.Property(r => r.ProductLabel).HasColumnName("product_label").HasMaxLength(300);
        b.Property(r => r.FieldName).HasColumnName("field_name").HasMaxLength(100);
        b.Property(r => r.FieldValue).HasColumnName("field_value").HasMaxLength(300);
        b.Property(r => r.DispositionId).HasColumnName("disposition_id");
        b.Property(r => r.DispositionCategoryId).HasColumnName("disposition_category_id");
        b.Property(r => r.DispositionLabel).HasColumnName("disposition_label").HasMaxLength(200);
        b.Property(r => r.TierLabel).HasColumnName("tier_label").HasMaxLength(50);
        b.Property(r => r.EffectiveFrom).HasColumnName("effective_from");
        b.Property(r => r.EffectiveUntil).HasColumnName("effective_until");
        b.Property(r => r.IsActive).HasColumnName("is_active");
        b.Property(r => r.CreatedAt).HasColumnName("created_at");
        b.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(r => r.CampaignId).HasDatabaseName("ix_commission_rules_campaign");
        b.HasIndex(r => r.ClientId).HasDatabaseName("ix_commission_rules_client");
    }
}

public class CommissionEntryConfiguration : IEntityTypeConfiguration<CommissionEntry>
{
    public void Configure(EntityTypeBuilder<CommissionEntry> b)
    {
        b.ToTable("commission_entries");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.TenantId).HasColumnName("tenant_id");
        b.Property(e => e.CallRecordId).HasColumnName("call_record_id");
        b.Property(e => e.AgentId).HasColumnName("agent_id");
        b.Property(e => e.ClientId).HasColumnName("client_id");
        b.Property(e => e.CampaignId).HasColumnName("campaign_id");
        b.Property(e => e.EntryType).HasColumnName("entry_type").HasMaxLength(20).IsRequired();
        b.Property(e => e.RuleId).HasColumnName("rule_id");
        b.Property(e => e.RuleName).HasColumnName("rule_name").HasMaxLength(200).IsRequired();
        b.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(30).IsRequired();
        b.Property(e => e.Basis).HasColumnName("basis").HasPrecision(14, 4);
        b.Property(e => e.Rate).HasColumnName("rate").HasPrecision(12, 4);
        b.Property(e => e.Amount).HasColumnName("amount").HasPrecision(12, 2);
        b.Property(e => e.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        b.Property(e => e.IsReversed).HasColumnName("is_reversed");
        b.Property(e => e.ReversesEntryId).HasColumnName("reverses_entry_id");
        b.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);
        b.Property(e => e.BatchId).HasColumnName("batch_id");
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
        b.HasOne<CallRecord>().WithMany().HasForeignKey(e => e.CallRecordId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(e => e.CallRecordId).HasDatabaseName("ix_commission_entries_call");
        b.HasIndex(e => new { e.AgentId, e.OccurredAt }).HasDatabaseName("ix_commission_entries_agent_time");
        b.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_commission_entries_time");
    }
}

public class CommissionRecalcBatchConfiguration : IEntityTypeConfiguration<CommissionRecalcBatch>
{
    public void Configure(EntityTypeBuilder<CommissionRecalcBatch> b)
    {
        b.ToTable("commission_recalc_batches");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.ClientId).HasColumnName("client_id");
        b.Property(x => x.CampaignId).HasColumnName("campaign_id");
        b.Property(x => x.AgentId).HasColumnName("agent_id");
        b.Property(x => x.From).HasColumnName("from_at");
        b.Property(x => x.To).HasColumnName("to_at");
        b.Property(x => x.PostTo).HasColumnName("post_to").HasMaxLength(20).IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        b.Property(x => x.RequestedBy).HasColumnName("requested_by").HasMaxLength(200);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(x => x.TotalCalls).HasColumnName("total_calls");
        b.Property(x => x.ProcessedCalls).HasColumnName("processed_calls");
        b.Property(x => x.ChangedCalls).HasColumnName("changed_calls");
        b.Property(x => x.Difference).HasColumnName("difference").HasPrecision(14, 2);
        b.Property(x => x.Error).HasColumnName("error").HasMaxLength(2000);
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.StartedAt).HasColumnName("started_at");
        b.Property(x => x.CompletedAt).HasColumnName("completed_at");
        b.HasIndex(x => x.Status).HasDatabaseName("ix_commission_recalc_batches_status");
    }
}
