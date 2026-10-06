using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class DispositionCategoryConfiguration : IEntityTypeConfiguration<DispositionCategory>
{
    public void Configure(EntityTypeBuilder<DispositionCategory> b)
    {
        b.ToTable("disposition_categories");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.TenantId).HasColumnName("tenant_id");
        b.Property(c => c.Key).HasColumnName("key").HasMaxLength(40);
        b.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(c => c.Description).HasColumnName("description").HasMaxLength(500);
        b.Property(c => c.SalesOpportunity).HasColumnName("sales_opportunity");
        b.Property(c => c.ExcludedFromKpis).HasColumnName("excluded_from_kpis");
        b.Property(c => c.DisplayOrder).HasColumnName("display_order");
        b.Property(c => c.IsActive).HasColumnName("is_active");
        b.Property(c => c.CreatedAt).HasColumnName("created_at");
        b.Ignore(c => c.IsSystem);
        b.HasIndex(c => c.Key).IsUnique().HasFilter("key IS NOT NULL").HasDatabaseName("ux_disposition_categories_key");
    }
}

public class CustomKpiConfiguration : IEntityTypeConfiguration<CustomKpi>
{
    public void Configure(EntityTypeBuilder<CustomKpi> b)
    {
        b.ToTable("custom_kpis");
        b.HasKey(k => k.Id);
        b.Property(k => k.Id).HasColumnName("id");
        b.Property(k => k.TenantId).HasColumnName("tenant_id");
        b.Property(k => k.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(k => k.Description).HasColumnName("description").HasMaxLength(500);
        b.Property(k => k.NumeratorCategoryIds).MapJson("numerator_category_ids", () => new List<Guid>()).HasDefaultValueSql("'[]'::jsonb").IsRequired();
        b.Property(k => k.DenominatorCategoryIds).MapJson("denominator_category_ids", () => new List<Guid>()).HasDefaultValueSql("'[]'::jsonb").IsRequired();
        b.Property(k => k.DisplayOrder).HasColumnName("display_order");
        b.Property(k => k.IsActive).HasColumnName("is_active");
        b.Property(k => k.CreatedAt).HasColumnName("created_at");
        b.Property(k => k.UpdatedAt).HasColumnName("updated_at");
    }
}

public class DispositionConfiguration : IEntityTypeConfiguration<Disposition>
{
    public void Configure(EntityTypeBuilder<Disposition> b)
    {
        b.ToTable("dispositions");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).HasColumnName("id");
        b.Property(d => d.TenantId).HasColumnName("tenant_id");
        b.Property(d => d.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(d => d.Code).HasColumnName("code").HasMaxLength(50);
        b.Property(d => d.CategoryId).HasColumnName("category_id");
        b.Property(d => d.ClientId).HasColumnName("client_id");
        b.Property(d => d.CampaignId).HasColumnName("campaign_id");
        b.Property(d => d.Aliases).MapJson("aliases", () => new List<string>()).HasDefaultValueSql("'[]'::jsonb").IsRequired();
        b.Property(d => d.DisplayOrder).HasColumnName("display_order");
        b.Property(d => d.IsActive).HasColumnName("is_active");
        b.Property(d => d.CreatedAt).HasColumnName("created_at");
        b.Property(d => d.UpdatedAt).HasColumnName("updated_at");
        b.Ignore(d => d.ScopeRank);
        b.HasOne<DispositionCategory>().WithMany().HasForeignKey(d => d.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(d => new { d.CampaignId, d.ClientId }).HasDatabaseName("idx_dispositions_scope");
    }
}
