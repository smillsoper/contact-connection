using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

/// <summary>Number port-in orders (S184) — public schema, so the Portal's porting queue sees every tenant's.</summary>
public class PortOrderConfiguration : IEntityTypeConfiguration<PortOrder>
{
    public void Configure(EntityTypeBuilder<PortOrder> b)
    {
        b.ToTable("port_orders");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).HasColumnName("id");
        b.Property(o => o.Number).HasColumnName("number").UseIdentityAlwaysColumn().HasIdentityOptions(startValue: 1001);
        b.HasIndex(o => o.Number).IsUnique().HasDatabaseName("ux_port_orders_number");
        b.Property(o => o.TenantId).HasColumnName("tenant_id");
        b.HasIndex(o => new { o.TenantId, o.CreatedAt }).HasDatabaseName("ix_port_orders_tenant_created");
        b.Property(o => o.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
        b.Property(o => o.Numbers).HasColumnName("numbers").HasColumnType("text[]");
        b.Property(o => o.Services).HasColumnName("services").HasMaxLength(60).IsRequired();
        b.Property(o => o.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        b.HasIndex(o => o.Status).HasDatabaseName("ix_port_orders_status");
        b.Property(o => o.AccountType).HasColumnName("account_type").HasMaxLength(20).IsRequired();
        b.Property(o => o.EndUserName).HasColumnName("end_user_name").HasMaxLength(200).IsRequired();
        b.Property(o => o.CurrentProviderHint).HasColumnName("current_provider_hint").HasMaxLength(200);
        b.Property(o => o.PreAssignCampaignId).HasColumnName("pre_assign_campaign_id");
        b.Property(o => o.PreAssignFlowId).HasColumnName("pre_assign_flow_id");
        b.Property(o => o.PreAssignTelephonyFlowId).HasColumnName("pre_assign_telephony_flow_id");
        b.Property(o => o.RequestedById).HasColumnName("requested_by_id");
        b.Property(o => o.RequestedByName).HasColumnName("requested_by_name").HasMaxLength(200).IsRequired();
        b.Property(o => o.RequestedByEmail).HasColumnName("requested_by_email").HasMaxLength(320).IsRequired();
        b.Property(o => o.SignerEmail).HasColumnName("signer_email").HasMaxLength(320).IsRequired();
        b.Property(o => o.Signer).MapJson<PortSignerDetails?>("signer", () => null);
        b.Property(o => o.PinProtected).HasColumnName("pin_protected");
        b.Property(o => o.PinNotApplicable).HasColumnName("pin_not_applicable");
        b.Property(o => o.BillFileKey).HasColumnName("bill_file_key").HasMaxLength(300);
        b.Property(o => o.BillFileName).HasColumnName("bill_file_name").HasMaxLength(200);
        b.Property(o => o.BillContentType).HasColumnName("bill_content_type").HasMaxLength(120);
        b.Property(o => o.SignatureName).HasColumnName("signature_name").HasMaxLength(200);
        b.Property(o => o.SignedAt).HasColumnName("signed_at");
        b.Property(o => o.SignerIp).HasColumnName("signer_ip").HasMaxLength(64);
        b.Property(o => o.SignerUserAgent).HasColumnName("signer_user_agent").HasMaxLength(300);
        b.Property(o => o.LoaFileKey).HasColumnName("loa_file_key").HasMaxLength(300);
        b.Property(o => o.LoaSha256).HasColumnName("loa_sha256").HasMaxLength(64);
        b.Property(o => o.CertificateFileKey).HasColumnName("certificate_file_key").HasMaxLength(300);
        b.Property(o => o.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
        b.HasIndex(o => o.TokenHash).HasDatabaseName("ix_port_orders_token_hash");
        b.Property(o => o.TokenExpiresAt).HasColumnName("token_expires_at");
        b.Property(o => o.CorrectionMessage).HasColumnName("correction_message").HasMaxLength(1000);
        b.Property(o => o.SignalWireOrderNumber).HasColumnName("signalwire_order_number").HasMaxLength(100);
        b.Property(o => o.FocDate).HasColumnName("foc_date");
        b.Property(o => o.NumbersLoadedAt).HasColumnName("numbers_loaded_at");
        b.Property(o => o.CompletedAt).HasColumnName("completed_at");
        b.Property(o => o.SensitivePurgedAt).HasColumnName("sensitive_purged_at");
        b.Property(o => o.Events).MapJson("events", () => new List<PortEvent>());
        b.Property(o => o.CreatedAt).HasColumnName("created_at");
        b.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        b.Ignore(o => o.Reference);
        b.Ignore(o => o.Label);
    }
}
