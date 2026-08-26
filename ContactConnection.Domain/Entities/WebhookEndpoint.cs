using System.Security.Cryptography;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// A standalone inbound webhook — a vendor pushes a payload here instead of us only ever
/// polling/calling out, and the payload is mapped onto one of this tenant's canonical domain
/// objects (see CanonicalWebhookType) per <see cref="MappingConfig"/>. Not tied to any
/// TenantApiDefinition/TenantApiEndpoint — a webhook is its own thing, not an operation attached
/// to an outbound API connection. See API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook
/// support" (original endpoint-scoped design shipped Session 87; replaced with this
/// canonical-object-mapping design Session 90 after a pre-merge design review).
///
/// The shared secret used to sign/verify inbound requests is deliberately NOT stored on this
/// entity — it lives in the existing ITenantCredentialStore under the deterministic key
/// $"webhook:{Id}", exactly like every other credential in this system. No new secret-storage
/// mechanism.
/// </summary>
public class WebhookEndpoint
{
    public Guid Id { get; private set; }

    /// <summary>Admin-given label — there's no owning API endpoint name to borrow anymore.</summary>
    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>One of CanonicalWebhookType's constants — which kind of domain object this
    /// webhook's payloads update.</summary>
    public string CanonicalType { get; private set; } = string.Empty;

    /// <summary>JSON mapping rule: root match (how to find the target record), an optional
    /// nested items-array config (for "one payload updates several child records" shapes, e.g.
    /// a fulfillment agency's multi-line-item status update), the operation to run on each
    /// matched record, and no-match/multiple-match edge-case policies. Evaluated by
    /// CanonicalWebhookMappingEvaluator. Default "{}" — a webhook with no mapping configured yet
    /// stores/logs every received event without dispatching anywhere.</summary>
    public string MappingConfig { get; private set; } = "{}";

    /// <summary>Opaque random token used in the public URL (/api/v1/webhooks/{Token}).</summary>
    public string Token { get; private set; } = string.Empty;

    public string SignatureHeaderName { get; private set; } = "X-Signature";

    /// <summary>SHA256/SHA512/SHA1/MD5 — same vocabulary as HmacSigner.</summary>
    public string SignatureAlgorithm { get; private set; } = "SHA256";

    /// <summary>
    /// Mirrors HmacSigner's outbound convention: when true, the expected signature header is the
    /// Stripe/Svix-style "t={seconds},v1={hex}" format, enabling replay-window rejection.
    /// </summary>
    public bool IncludeTimestamp { get; private set; } = true;

    public int TimestampToleranceSeconds { get; private set; } = 300;

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private WebhookEndpoint() { }

    public static WebhookEndpoint Create(string name, string canonicalType, string? description = null)
    {
        if (!CanonicalWebhookType.All.Contains(canonicalType))
            throw new ArgumentException($"Unknown canonical webhook type '{canonicalType}'.", nameof(canonicalType));

        return new WebhookEndpoint
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description?.Trim(),
            CanonicalType = canonicalType,
            Token = GenerateToken(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, string? description)
    {
        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetMappingConfig(string mappingConfigJson)
    {
        MappingConfig = mappingConfigJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RegenerateToken()
    {
        Token = GenerateToken();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetSignatureConfig(string headerName, string algorithm, bool includeTimestamp, int toleranceSeconds)
    {
        SignatureHeaderName = string.IsNullOrWhiteSpace(headerName) ? "X-Signature" : headerName.Trim();
        SignatureAlgorithm = string.IsNullOrWhiteSpace(algorithm) ? "SHA256" : algorithm.Trim().ToUpperInvariant();
        IncludeTimestamp = includeTimestamp;
        TimestampToleranceSeconds = toleranceSeconds > 0 ? toleranceSeconds : 300;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate() { IsActive = true; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTimeOffset.UtcNow; }

    private static string GenerateToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

    /// <summary>Deterministic credential-store key for this webhook's shared secret.</summary>
    public string CredentialKeyName => $"webhook:{Id}";
}
