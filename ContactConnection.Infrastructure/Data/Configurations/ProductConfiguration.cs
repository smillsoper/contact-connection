using System.Text.Json;
using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);

        // Identity
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TenantId).HasColumnName("tenant_id");

        // Core catalog
        builder.Property(p => p.Sku).HasColumnName("sku").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(p => p.Searchable).HasColumnName("searchable");
        builder.Property(p => p.ReportingOnly).HasColumnName("reporting_only");
        builder.Property(p => p.TaxCode).HasColumnName("tax_code").HasMaxLength(50);
        builder.Property(p => p.ClientId).HasColumnName("client_id");
        builder.Property(p => p.CampaignIds).HasColumnName("campaign_ids").IsRequired();   // uuid[]
        builder.Property(p => p.ParentProductId).HasColumnName("parent_product_id");

        // Physical
        builder.Property(p => p.Weight).HasColumnName("weight").HasColumnType("numeric(10,4)");

        // Geographic surcharges
        builder.Property(p => p.CanadaSurcharge).HasColumnName("canada_surcharge").HasColumnType("numeric(18,2)");
        builder.Property(p => p.AKHISurcharge).HasColumnName("akhi_surcharge").HasColumnType("numeric(18,2)");
        builder.Property(p => p.OutlyingUSSurcharge).HasColumnName("outlying_us_surcharge").HasColumnType("numeric(18,2)");
        builder.Property(p => p.ForeignSurcharge).HasColumnName("foreign_surcharge").HasColumnType("numeric(18,2)");

        // Inventory
        builder.Property(p => p.InventoryStatus)
            .HasColumnName("inventory_status")
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(p => p.DecrementOnOrder).HasColumnName("decrement_on_order");
        builder.Property(p => p.QtyAvailable).HasColumnName("qty_available");
        builder.Property(p => p.QtyReserved).HasColumnName("qty_reserved");
        builder.Property(p => p.MinimumQty).HasColumnName("minimum_qty");
        builder.Property(p => p.QtyLimit).HasColumnName("qty_limit");
        builder.Property(p => p.QtyLimitException).HasColumnName("qty_limit_exception");
        builder.Property(p => p.ExpectedQuantity).HasColumnName("expected_quantity");
        builder.Property(p => p.ExpectedStockDate).HasColumnName("expected_stock_date");
        builder.Property(p => p.BackorderMessage).HasColumnName("backorder_message").HasMaxLength(500);
        builder.Property(p => p.DiscontinuedMessage).HasColumnName("discontinued_message").HasMaxLength(500);

        // Search / discovery — JSONB
        builder.Property(p => p.AliasSKUs)
            .HasColumnName("alias_skus").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(p => p.Keywords)
            .HasColumnName("keywords").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        // Audit
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        // Indexes
        builder.HasIndex(p => p.Sku).IsUnique().HasDatabaseName("ix_products_sku");
        builder.HasIndex(p => p.ParentProductId).HasDatabaseName("ix_products_parent_product_id");
        builder.HasIndex(p => p.ClientId).HasDatabaseName("ix_products_scope");
        builder.HasIndex(p => new { p.Searchable, p.InventoryStatus })
            .HasDatabaseName("ix_products_searchable_status");

        // Self-referential FK for variants
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(p => p.ParentProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Kits (cascade — deleting a product removes its kit entries)
        builder.HasMany(p => p.Kits)
            .WithOne(k => k.Parent)
            .HasForeignKey(k => k.ParentProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Offers (cascade — deleting a product removes its offers)
        builder.HasMany(p => p.Offers)
            .WithOne(o => o.Product)
            .HasForeignKey(o => o.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Categories — many-to-many via product_category_map join table
        builder.HasMany(p => p.Categories)
            .WithMany()
            .UsingEntity(
                "product_category_map",
                l => l.HasOne(typeof(ProductCategory)).WithMany()
                       .HasForeignKey("category_id").HasPrincipalKey(nameof(ProductCategory.Id)),
                r => r.HasOne(typeof(Product)).WithMany()
                       .HasForeignKey("product_id").HasPrincipalKey(nameof(Product.Id)),
                j =>
                {
                    j.HasKey("product_id", "category_id");
                    j.ToTable("product_category_map");
                    j.HasIndex("product_id").HasDatabaseName("ix_product_category_map_product_id");
                    j.HasIndex("category_id").HasDatabaseName("ix_product_category_map_category_id");
                });

        // Attribute values — many-to-many via product_attribute_assignments join table
        builder.HasMany(p => p.AttributeValues)
            .WithMany()
            .UsingEntity(
                "product_attribute_assignments",
                l => l.HasOne(typeof(ProductAttributeValue)).WithMany()
                       .HasForeignKey("attribute_value_id").HasPrincipalKey(nameof(ProductAttributeValue.Id)),
                r => r.HasOne(typeof(Product)).WithMany()
                       .HasForeignKey("product_id").HasPrincipalKey(nameof(Product.Id)),
                j =>
                {
                    j.HasKey("product_id", "attribute_value_id");
                    j.ToTable("product_attribute_assignments");
                    j.HasIndex("product_id").HasDatabaseName("ix_product_attribute_assignments_product_id");
                    j.HasIndex("attribute_value_id").HasDatabaseName("ix_product_attribute_assignments_value_id");
                });
    }
}
