using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", "public");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.TenantId).HasColumnName("tenant_id");
        builder.Property(i => i.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
        builder.Property(i => i.Number).HasColumnName("number").HasMaxLength(20);
        builder.Property(i => i.CreditsInvoiceId).HasColumnName("credits_invoice_id");
        builder.Property(i => i.PeriodStart).HasColumnName("period_start");
        builder.Property(i => i.PeriodEnd).HasColumnName("period_end");
        builder.Property(i => i.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(i => i.Total).HasColumnName("total").HasPrecision(12, 2);
        builder.Property(i => i.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(i => i.BillToName).HasColumnName("bill_to_name").HasMaxLength(200);
        builder.Property(i => i.BillToEmail).HasColumnName("bill_to_email").HasMaxLength(500);
        builder.Property(i => i.IssuedAt).HasColumnName("issued_at");
        builder.Property(i => i.DueOn).HasColumnName("due_on");
        builder.Property(i => i.PaidAt).HasColumnName("paid_at");
        builder.Property(i => i.PaymentReference).HasColumnName("payment_reference").HasMaxLength(200);
        builder.Property(i => i.VoidedAt).HasColumnName("voided_at");
        builder.Property(i => i.VoidReason).HasColumnName("void_reason").HasMaxLength(500);
        builder.Property(i => i.StripeInvoiceId).HasColumnName("stripe_invoice_id").HasMaxLength(100);
        builder.Property(i => i.StripePaymentIntentId).HasColumnName("stripe_payment_intent_id").HasMaxLength(100);
        builder.Property(i => i.PaymentState).HasColumnName("payment_state").HasMaxLength(20);
        builder.Property(i => i.PaymentError).HasColumnName("payment_error").HasMaxLength(500);
        builder.Property(i => i.PaymentAttempts).HasColumnName("payment_attempts").HasDefaultValue(0);
        builder.HasIndex(i => i.StripePaymentIntentId).HasDatabaseName("ix_invoices_stripe_payment_intent");
        builder.Property(i => i.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(i => i.IsDraft);

        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => i.Number).IsUnique().HasDatabaseName("ux_invoices_number");
        builder.HasIndex(i => new { i.TenantId, i.CreatedAt }).HasDatabaseName("ix_invoices_tenant_created");
        // One live monthly invoice per tenant per period (a voided one can be replaced).
        builder.HasIndex(i => new { i.TenantId, i.PeriodStart })
            .IsUnique()
            .HasFilter("kind = 'invoice' AND status <> 'void' AND period_start IS NOT NULL")
            .HasDatabaseName("ux_invoices_tenant_period");
    }
}

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines", "public");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.InvoiceId).HasColumnName("invoice_id");
        builder.Property(l => l.SortOrder).HasColumnName("sort_order");
        builder.Property(l => l.Kind).HasColumnName("kind").HasMaxLength(30).IsRequired();
        builder.Property(l => l.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(l => l.Quantity).HasColumnName("quantity").HasPrecision(14, 2);
        builder.Property(l => l.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 4);
        builder.Property(l => l.Amount).HasColumnName("amount").HasPrecision(12, 2);
        builder.Property(l => l.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(l => l.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
    }
}
