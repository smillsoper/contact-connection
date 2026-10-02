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
        b.Property(r => r.TierLabel).HasColumnName("tier_label").HasMaxLength(50);
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
        b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
        b.HasOne<CallRecord>().WithMany().HasForeignKey(e => e.CallRecordId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(e => e.CallRecordId).HasDatabaseName("ix_commission_entries_call");
        b.HasIndex(e => new { e.AgentId, e.OccurredAt }).HasDatabaseName("ix_commission_entries_agent_time");
        b.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_commission_entries_time");
    }
}
