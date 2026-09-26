namespace ContactConnection.Application.Interfaces.Services;

/// <summary>Result of a gateway authorize call — provider-agnostic shape every
/// IPaymentGatewayClient implementation maps its own response into.</summary>
public record GatewayAuthResult(
    bool Succeeded,
    string Status, // PaymentTransactionStatus: approved | declined | error
    string? GatewayTransactionId,
    string? AuthCode,
    string? ResponseCode,
    string? ResponseReasonText,
    string? AvsResultCode,
    string? CvvResultCode,
    string? CardLast4,
    string? CardType);

public record GatewayVoidResult(bool Succeeded, string? ResponseReasonText);

/// <summary>
/// Talks to exactly one payment gateway's HTTP API. Owns nothing else — no PaymentTransaction
/// persistence, no CallRecord access, no sensitive-data wipe. See IPaymentService for the
/// orchestration every node handler actually calls. Each implementation owns its own credential key
/// naming (different gateways need different credential shapes), resolved via ITenantCredentialStore
/// using the campaign -> client -> tenant cascade.
/// </summary>
public interface IPaymentGatewayClient
{
    /// <summary>Dispatch key for IPaymentGatewayClientFactory — e.g. "authorize_net".</summary>
    string ProviderKey { get; }

    Task<GatewayAuthResult> AuthorizeAsync(
        Guid campaignId, Guid clientId, decimal amount,
        string cardNumber, string expirationMMYY, string cvv, string? zip,
        CancellationToken ct = default);

    /// <summary>Voids a previously-authorized transaction. No card data needed — just the original
    /// gateway transaction id and the same credential scope that created it.</summary>
    Task<GatewayVoidResult> VoidAsync(
        Guid campaignId, Guid clientId, string gatewayTransactionId,
        CancellationToken ct = default);
}

/// <summary>Resolves the correct IPaymentGatewayClient by ProviderKey. Unlike ITaxProviderFactory,
/// throws on an unrecognized/unconfigured key rather than silently falling back to a default — a
/// wrong gateway silently used for a real charge is a much worse failure mode than tax defaulting to
/// flat-rate.</summary>
public interface IPaymentGatewayClientFactory
{
    IPaymentGatewayClient Resolve(string providerKey);
}
