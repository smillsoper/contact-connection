using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

public class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.WebhookEndpointId).HasColumnName("webhook_endpoint_id").IsRequired();
        builder.Property(e => e.ReceivedAt).HasColumnName("received_at").IsRequired();
        builder.Property(e => e.SignatureValid).HasColumnName("signature_valid").IsRequired();
        builder.Property(e => e.RawBody).HasColumnName("raw_body").HasColumnType("text").IsRequired();
        builder.Property(e => e.ContentType).HasColumnName("content_type").HasMaxLength(200);
        builder.Property(e => e.BodyHash).HasColumnName("body_hash").HasMaxLength(64).IsRequired();
        builder.Property(e => e.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.ProcessingError).HasColumnName("processing_error").HasMaxLength(2000);
        builder.Property(e => e.OutcomeKey).HasColumnName("outcome_key").HasMaxLength(100);
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");

        // Unique — defense-in-depth against a concurrent-duplicate race slipping past the
        // application-level ExistsAsync check in WebhookReceiveHandler.
        builder.HasIndex(e => new { e.WebhookEndpointId, e.BodyHash }).IsUnique().HasDatabaseName("ix_webhook_events_endpoint_body_hash");
        builder.HasIndex(e => e.WebhookEndpointId).HasDatabaseName("ix_webhook_events_webhook_endpoint_id");

        // Shadow FK (no navigation property on either side — WebhookEvent is an append-only
        // receipt log, not something a WebhookEndpoint needs to eagerly load) with a real
        // DB-level cascade, matching OrderLine→Order/CallInteraction→CallRecord's convention:
        // deleting a webhook must not leave orphaned event rows behind. Found live during
        // Session 90's redesign verification — WebhookEndpointId previously had no FK
        // relationship configured at all, so AdminWebhooksEndpoints' Delete left every event
        // row behind un-cascaded.
        builder.HasOne<WebhookEndpoint>()
            .WithMany()
            .HasForeignKey(e => e.WebhookEndpointId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
