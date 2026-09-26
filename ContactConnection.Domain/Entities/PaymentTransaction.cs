namespace ContactConnection.Domain.Entities;

/// <summary>
/// Audit/idempotency record of one gateway call (auth-only today; auth+capture/capture reserved for
/// later — see <see cref="PaymentTransactionType"/>). Deliberately never stores the PAN or CVV —
/// only a masked last-4 and card type, both safe to persist. <see cref="ClientId"/>/
/// <see cref="CampaignId"/>/<see cref="Gateway"/> are carried alongside the result specifically so a
/// later void can re-resolve the exact same credential scope the original authorization used.
/// </summary>
public class PaymentTransaction
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CallRecordId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid CampaignId { get; private set; }

    public string Gateway { get; private set; } = "";
    public string TransactionType { get; private set; } = PaymentTransactionType.AuthOnly;
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = "USD";
    public string Status { get; private set; } = "";

    public string? GatewayTransactionId { get; private set; }
    public string? AuthCode { get; private set; }
    public string? ResponseCode { get; private set; }
    public string? ResponseReasonText { get; private set; }
    public string? AvsResultCode { get; private set; }
    public string? CvvResultCode { get; private set; }

    public string? CardLast4 { get; private set; }
    public string? CardType { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }

    // Required by EF Core
    private PaymentTransaction() { }

    public static PaymentTransaction Create(
        Guid id, Guid tenantId, Guid callRecordId, Guid clientId, Guid campaignId,
        string gateway, decimal amount, string status,
        string? gatewayTransactionId, string? authCode, string? responseCode,
        string? responseReasonText, string? avsResultCode, string? cvvResultCode,
        string? cardLast4, string? cardType) => new()
    {
        Id                   = id,
        TenantId             = tenantId,
        CallRecordId         = callRecordId,
        ClientId             = clientId,
        CampaignId           = campaignId,
        Gateway              = gateway,
        TransactionType      = PaymentTransactionType.AuthOnly,
        Amount               = amount,
        Status               = status,
        GatewayTransactionId = gatewayTransactionId,
        AuthCode             = authCode,
        ResponseCode         = responseCode,
        ResponseReasonText   = responseReasonText,
        AvsResultCode        = avsResultCode,
        CvvResultCode        = cvvResultCode,
        CardLast4            = cardLast4,
        CardType             = cardType,
        CreatedAt            = DateTimeOffset.UtcNow,
    };

    public void MarkVoided() => VoidedAt = DateTimeOffset.UtcNow;
}

public static class PaymentTransactionType
{
    public const string AuthOnly    = "auth_only";
    public const string AuthCapture = "auth_capture"; // reserved — not produced yet
    public const string Capture     = "capture";      // reserved — not produced yet
}

public static class PaymentTransactionStatus
{
    public const string Approved = "approved";
    public const string Declined = "declined";
    public const string Error    = "error";
}
