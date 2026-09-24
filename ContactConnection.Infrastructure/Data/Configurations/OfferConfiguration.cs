using System.Text.Json;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("offers");
        builder.HasKey(o => o.Id);

        // Identity
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.TenantId).HasColumnName("tenant_id");
        builder.Property(o => o.ProductId).HasColumnName("product_id");
        builder.Property(o => o.Name).HasColumnName("name").HasMaxLength(200).IsRequired();

        // Scope — null = tenant-wide
        builder.Property(o => o.ClientId).HasColumnName("client_id");
        builder.Property(o => o.CampaignId).HasColumnName("campaign_id");

        // Pricing
        builder.Property(o => o.FullPrice).HasColumnName("full_price").HasColumnType("numeric(18,2)");
        builder.Property(o => o.AllowPriceOverride).HasColumnName("allow_price_override");

        // Shipping per offer
        builder.Property(o => o.Shipping).HasColumnName("shipping").HasColumnType("numeric(18,2)");
        builder.Property(o => o.TaxExempt).HasColumnName("tax_exempt");
        builder.Property(o => o.ShippingExempt).HasColumnName("shipping_exempt");

        // Mix & match
        builder.Property(o => o.MixMatchCode).HasColumnName("mix_match_code").HasMaxLength(100);

        // Upsell
        builder.Property(o => o.IsUpsell).HasColumnName("is_upsell");
        builder.Property(o => o.UpsellQty).HasColumnName("upsell_qty");
        builder.Property(o => o.UpsellQtyOfEntry).HasColumnName("upsell_qty_of_entry");
        builder.Property(o => o.UpsellCommission).HasColumnName("upsell_commission").HasColumnType("numeric(18,2)");
        builder.Property(o => o.UpsellClientAmount).HasColumnName("upsell_client_amount").HasColumnType("numeric(18,2)");

        // AutoShip
        builder.Property(o => o.AutoShip).HasColumnName("auto_ship");
        builder.Property(o => o.AutoShipOptional).HasColumnName("auto_ship_optional");

        // Ship-to / delivery
        builder.Property(o => o.AllowShipTo).HasColumnName("allow_ship_to");
        builder.Property(o => o.ShipToRequired).HasColumnName("ship_to_required");
        builder.Property(o => o.AllowDeliveryMessage).HasColumnName("allow_delivery_message");
        builder.Property(o => o.ShipMethodPerItem).HasColumnName("ship_method_per_item");

        // Availability
        builder.Property(o => o.IsActive).HasColumnName("is_active");
        builder.Property(o => o.ValidFrom).HasColumnName("valid_from");
        builder.Property(o => o.ValidTo).HasColumnName("valid_to");

        // JSONB columns
        builder.Property(o => o.Payments)
            .HasColumnName("payments").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<PaymentInstallment>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(o => o.QuantityPriceBreaks)
            .HasColumnName("quantity_price_breaks").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<QuantityPriceBreak>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(o => o.MixMatchPriceBreaks)
            .HasColumnName("mix_match_price_breaks").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<QuantityPriceBreak>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(o => o.AutoShipIntervals)
            .HasColumnName("auto_ship_intervals").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<AutoShipInterval>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(o => o.ShipMethods)
            .HasColumnName("ship_methods").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<ProductShipMethod>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(o => o.Personalization)
            .HasColumnName("personalization").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<PersonalizationPrompt>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(o => o.Flags)
            .HasColumnName("flags").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<ProductFlag>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        // Audit
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");

        // Indexes
        builder.HasIndex(o => o.ProductId).HasDatabaseName("ix_offers_product_id");
        builder.HasIndex(o => o.MixMatchCode).HasDatabaseName("ix_offers_mix_match_code");
        builder.HasIndex(o => new { o.TenantId, o.IsActive }).HasDatabaseName("ix_offers_tenant_active");
        builder.HasIndex(o => new { o.TenantId, o.ProductId, o.ClientId, o.CampaignId }).HasDatabaseName("ix_offers_scope");

        // FK — Product is configured from ProductConfiguration (cascade delete)
    }
}
