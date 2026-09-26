using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("payment_transactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.TenantId).HasColumnName("tenant_id");
        builder.Property(t => t.CallRecordId).HasColumnName("call_record_id");
        builder.Property(t => t.ClientId).HasColumnName("client_id");
        builder.Property(t => t.CampaignId).HasColumnName("campaign_id");

        builder.Property(t => t.Gateway).HasColumnName("gateway").HasMaxLength(50).IsRequired();
        builder.Property(t => t.TransactionType).HasColumnName("transaction_type").HasMaxLength(50).IsRequired();
        builder.Property(t => t.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
        builder.Property(t => t.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        builder.Property(t => t.GatewayTransactionId).HasColumnName("gateway_transaction_id").HasMaxLength(100);
        builder.Property(t => t.AuthCode).HasColumnName("auth_code").HasMaxLength(50);
        builder.Property(t => t.ResponseCode).HasColumnName("response_code").HasMaxLength(20);
        builder.Property(t => t.ResponseReasonText).HasColumnName("response_reason_text").HasMaxLength(500);
        builder.Property(t => t.AvsResultCode).HasColumnName("avs_result_code").HasMaxLength(10);
        builder.Property(t => t.CvvResultCode).HasColumnName("cvv_result_code").HasMaxLength(10);

        builder.Property(t => t.CardLast4).HasColumnName("card_last4").HasMaxLength(4);
        builder.Property(t => t.CardType).HasColumnName("card_type").HasMaxLength(30);

        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.VoidedAt).HasColumnName("voided_at");

        builder.HasIndex(t => t.CallRecordId).HasDatabaseName("ix_payment_transactions_call_record_id");
        builder.HasIndex(t => new { t.TenantId, t.CreatedAt }).HasDatabaseName("ix_payment_transactions_tenant_created");
    }
}
