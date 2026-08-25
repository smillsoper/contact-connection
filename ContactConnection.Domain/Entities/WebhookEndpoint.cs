using System.Security.Cryptography;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// Inbound webhook configuration for a single TenantApiEndpoint (1:1, unique FK) — lets a vendor
/// push events (e.g. fulfillment tracking updates) instead of us only ever polling/calling out.
/// See API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support".
///
/// The shared secret used to sign/verify inbound requests is deliberately NOT stored on this
/// entity — it lives in the existing ITenantCredentialStore under the deterministic key
/// $"webhook:{Id}", exactly like every other credential in this system. No new secret-storage
/// mechanism.
/// </summary>
public class WebhookEndpoint
{
    public Guid Id { get; private set; }
    public Guid TenantApiEndpointId { get; private set; }

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

    public static WebhookEndpoint Create(Guid tenantApiEndpointId)
    {
        return new WebhookEndpoint
        {
            Id = Guid.NewGuid(),
            TenantApiEndpointId = tenantApiEndpointId,
            Token = GenerateToken(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
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
