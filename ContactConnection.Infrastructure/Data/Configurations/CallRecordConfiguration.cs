using System.Text.Json;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Domain.ValueObjects.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class CallRecordConfiguration : IEntityTypeConfiguration<CallRecord>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<CallRecord> builder)
    {
        builder.ToTable("call_records");
        builder.HasKey(r => r.Id);

        // Identity
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.TenantId).HasColumnName("tenant_id");
        builder.Property(r => r.ClientId).HasColumnName("client_id");
        builder.Property(r => r.CampaignId).HasColumnName("campaign_id");
        builder.Property(r => r.AgentId).HasColumnName("agent_id");

        // Call metadata
        builder.Property(r => r.Source)
            .HasColumnName("source").IsRequired().HasMaxLength(20);
        builder.Property(r => r.RecordType)
            .HasColumnName("record_type").IsRequired().HasMaxLength(20);
        builder.Property(r => r.OverallStatus)
            .HasColumnName("overall_status").IsRequired().HasMaxLength(20);

        // Caller identity
        builder.Property(r => r.CallerId).HasColumnName("caller_id").HasMaxLength(30);
        builder.Property(r => r.Dnis).HasColumnName("dnis").HasMaxLength(32);
        builder.Property(r => r.AccountNumber).HasColumnName("account_number").HasMaxLength(50);
        builder.Property(r => r.FirstName).HasColumnName("first_name").HasMaxLength(100);
        builder.Property(r => r.LastName).HasColumnName("last_name").HasMaxLength(100);
        builder.Property(r => r.Email).HasColumnName("email").HasMaxLength(254);
        builder.Property(r => r.Phone).HasColumnName("phone").HasMaxLength(20);

        // Timing
        builder.Property(r => r.CallStartAt).HasColumnName("call_start_at");
        builder.Property(r => r.CallEndAt).HasColumnName("call_end_at");
        builder.Property(r => r.HandleTimeSeconds)
            .HasColumnName("handle_time_seconds")
            .HasComputedColumnSql(
                "EXTRACT(EPOCH FROM (call_end_at - call_start_at))::integer",
                stored: true);

        // Financial
        builder.Property(r => r.TotalAmount).HasColumnName("total_amount").HasPrecision(10, 2);
        builder.Property(r => r.TaxAmount).HasColumnName("tax_amount").HasPrecision(10, 2);
        builder.Property(r => r.PaymentStatus).HasColumnName("payment_status").HasMaxLength(30);

        // Fulfillment
        builder.Property(r => r.FulfillmentStatus).HasColumnName("fulfillment_status").HasMaxLength(30);
        builder.Property(r => r.TrackingNumber).HasColumnName("tracking_number").HasMaxLength(100);

        // Telephony
        builder.Property(r => r.ContactIdExternal).HasColumnName("contact_id_external").HasMaxLength(100);
        builder.Property(r => r.RecordingUrl).HasColumnName("recording_url").HasMaxLength(500);

        // Recording lifecycle (denormalised from recording_events)
        builder.Property(r => r.RecordingStartedAt).HasColumnName("recording_started_at");
        builder.Property(r => r.RecordingStoppedAt).HasColumnName("recording_stopped_at");
        builder.Property(r => r.RecordingMaskedSeconds).HasColumnName("recording_masked_seconds").HasDefaultValue(0);
        builder.Property(r => r.RecordingRetained).HasColumnName("recording_retained").HasDefaultValue(true);
        builder.Property(r => r.RecordingDeletedAt).HasColumnName("recording_deleted_at");
        builder.Property(r => r.RecordingDeleteReason).HasColumnName("recording_delete_reason").HasMaxLength(200);

        // JSONB — typed
        builder.Property(r => r.Addresses)
            .HasColumnName("addresses")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<CallAddresses>(v, JsonOptions));

        builder.Property(r => r.CommitmentEvents)
            .HasColumnName("commitment_events")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<CommitmentEvent>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(r => r.RecordingEvents)
            .HasColumnName("recording_events")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<RecordingEvent>>(v, JsonOptions) ?? new())
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(r => r.Cart)
            .HasColumnName("cart")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<CartDocument>(v, JsonOptions));

        // JSONB — opaque strings (owned by future engine components)
        builder.Property(r => r.FlowExecutionState)
            .HasColumnName("flow_execution_state")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(r => r.CustomFields)
            .HasColumnName("custom_fields")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(r => r.ApiResponseCache)
            .HasColumnName("api_response_cache")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(r => r.TelephonyEvents)
            .HasColumnName("telephony_events")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");

        // Sensitive data (PCI — encrypted at rest)
        builder.Property(r => r.SensitiveData)
            .HasColumnName("sensitive_data")
            .HasColumnType("jsonb");
        builder.Property(r => r.SensitiveDataStoredAt).HasColumnName("sensitive_data_stored_at");
        builder.Property(r => r.SensitiveDataWipedAt).HasColumnName("sensitive_data_wiped_at");
        builder.Property(r => r.SensitiveWipeReason)
            .HasColumnName("sensitive_wipe_reason").HasMaxLength(100);

        // Audit
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        // Navigation
        builder.HasMany(r => r.Interactions)
            .WithOne()
            .HasForeignKey(i => i.CallRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes — per ARCHITECTURE.md §19
        builder.HasIndex(r => r.TenantId)
            .HasDatabaseName("idx_call_records_tenant");
        builder.HasIndex(r => new { r.CampaignId, r.CallStartAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_call_records_campaign_date");
        builder.HasIndex(r => new { r.AgentId, r.CallStartAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_call_records_agent_date");
        builder.HasIndex(r => new { r.TenantId, r.CallerId })
            .HasDatabaseName("idx_call_records_caller");
        builder.HasIndex(r => new { r.TenantId, r.AccountNumber })
            .HasDatabaseName("idx_call_records_account");
        builder.HasIndex(r => new { r.TenantId, r.AgentId })
            .HasFilter("overall_status = 'active'")
            .HasDatabaseName("idx_call_records_active");
    }
}
