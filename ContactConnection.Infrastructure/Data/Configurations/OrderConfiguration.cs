using System.Text.Json;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);

        // Identity
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.TenantId).HasColumnName("tenant_id");
        builder.Property(o => o.CallRecordId).HasColumnName("call_record_id").IsRequired(false);
        builder.Property(o => o.InteractionId).HasColumnName("interaction_id");

        // Lifecycle
        builder.Property(o => o.Status).HasColumnName("status").HasMaxLength(50).IsRequired();

        // Financial snapshot
        builder.Property(o => o.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(18,2)");
        builder.Property(o => o.Shipping).HasColumnName("shipping").HasColumnType("numeric(18,2)");
        builder.Property(o => o.SalesTax).HasColumnName("sales_tax").HasColumnType("numeric(18,2)");
        builder.Property(o => o.Discount).HasColumnName("discount").HasColumnType("numeric(18,2)");
        builder.Property(o => o.Total).HasColumnName("total").HasColumnType("numeric(18,2)");

        // Cart-level shipping
        builder.Property(o => o.ShipMethod).HasColumnName("ship_method").HasMaxLength(100);
        builder.Property(o => o.ShippingZip).HasColumnName("shipping_zip").HasMaxLength(20);

        // JSONB — payment plan snapshot
        builder.Property(o => o.PaymentBreakdowns)
            .HasColumnName("payment_breakdowns").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<CartPaymentBreakdown>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        // Fulfillment timestamps
        builder.Property(o => o.ShippedAt).HasColumnName("shipped_at");
        builder.Property(o => o.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(o => o.CancelledAt).HasColumnName("cancelled_at");

        // Audit
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");

        // Relationships
        builder.HasMany(o => o.Lines)
            .WithOne(l => l.Order)
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(o => o.CallRecordId).HasDatabaseName("ix_orders_call_record_id");
        builder.HasIndex(o => o.TenantId).HasDatabaseName("ix_orders_tenant_id");
        builder.HasIndex(o => new { o.TenantId, o.Status }).HasDatabaseName("ix_orders_tenant_status");
    }
}
