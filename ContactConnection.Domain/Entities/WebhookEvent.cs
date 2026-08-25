using System.Security.Cryptography;
using System.Text;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// Append-only receipt log — one row per inbound POST to a WebhookEndpoint. Durable audit trail
/// plus the dedup mechanism for vendor retry storms (BodyHash). See
/// API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support".
/// </summary>
public class WebhookEvent
{
    public Guid Id { get; private set; }
    public Guid WebhookEndpointId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public bool SignatureValid { get; private set; }

    /// <summary>Raw request body, verbatim. text column (not jsonb) — must tolerate non-JSON
    /// or malformed bodies without a write-time parse failure.</summary>
    public string RawBody { get; private set; } = string.Empty;

    public string? ContentType { get; private set; }

    /// <summary>SHA256 hex of RawBody — always computed, the dedup key for exact-duplicate
    /// vendor redeliveries.</summary>
    public string BodyHash { get; private set; } = string.Empty;

    public string ProcessingStatus { get; private set; } = WebhookEventStatus.Received;
    public string? ProcessingError { get; private set; }

    /// <summary>The outcome name matched by the response-mapping evaluator, if any — for the
    /// events UI, independent of whether it led to a successful dispatch.</summary>
    public string? OutcomeKey { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    private WebhookEvent() { }

    public static WebhookEvent Create(Guid webhookEndpointId, string rawBody, string? contentType, bool signatureValid)
    {
        return new WebhookEvent
        {
            Id = Guid.NewGuid(),
            WebhookEndpointId = webhookEndpointId,
            ReceivedAt = DateTimeOffset.UtcNow,
            SignatureValid = signatureValid,
            RawBody = rawBody,
            ContentType = contentType,
            BodyHash = ComputeBodyHash(rawBody),
            ProcessingStatus = WebhookEventStatus.Received,
        };
    }

    public static string ComputeBodyHash(string rawBody) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody ?? string.Empty)));

    public void MarkRejected(string reason)
    {
        ProcessingStatus = WebhookEventStatus.Rejected;
        ProcessingError = reason;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public void MarkDuplicate()
    {
        ProcessingStatus = WebhookEventStatus.Duplicate;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records the matched outcome without changing ProcessingStatus — used when a
    /// sub-type has no wired dispatch target yet, so the event stays "received" (stored/logged
    /// only) but the events UI can still show which outcome the payload matched.</summary>
    public void SetOutcomeKey(string? outcomeKey) => OutcomeKey = outcomeKey;

    public void MarkProcessed(string? outcomeKey)
    {
        ProcessingStatus = WebhookEventStatus.Processed;
        OutcomeKey = outcomeKey;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string? outcomeKey, string error)
    {
        ProcessingStatus = WebhookEventStatus.Failed;
        OutcomeKey = outcomeKey;
        ProcessingError = error;
        ProcessedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Lifecycle states for a received WebhookEvent.</summary>
public static class WebhookEventStatus
{
    public const string Received  = "received";
    public const string Processed = "processed";
    public const string Duplicate = "duplicate";
    public const string Rejected  = "rejected";
    public const string Failed    = "failed";
}
