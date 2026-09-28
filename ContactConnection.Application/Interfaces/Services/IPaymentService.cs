namespace ContactConnection.Application.Interfaces.Services;

public record PaymentAuthResult(
    bool Succeeded,
    string Status, // PaymentTransactionStatus: approved | declined | error
    Guid? TransactionId,
    string? GatewayTransactionId,
    string? AuthCode,
    string? ResponseReasonText,
    string? OrderNumber = null,
    // S164: what the node actually did (PaymentAuthAction), the amount now authorized, and the card's
    // last 4 (safe to show — lets a closing read-back confirm the card without the agent seeing it).
    string Action = PaymentAuthAction.Authorized,
    decimal? Amount = null,
    string? CardLast4 = null);

/// <summary>What AuthorizeAsync did on this pass — an authorize_payment node can be reached more than
/// once (an agent going back to change the order after a successful auth).</summary>
public static class PaymentAuthAction
{
    /// <summary>No live authorization existed — a new one was requested.</summary>
    public const string Authorized = "authorized";
    /// <summary>A live authorization existed for a different amount (or a newly captured card) — it
    /// was voided and a new one requested.</summary>
    public const string Reauthorized = "reauthorized";
    /// <summary>A live authorization for this exact amount and card capture already exists — nothing
    /// was sent to the gateway.</summary>
    public const string AlreadyAuthorized = "already_authorized";
}

public record PaymentVoidResult(bool Succeeded, string? ResponseReasonText);

/// <summary>
/// The orchestrator every node handler calls (mirrors how node handlers call ICartService, never
/// touch inventory/pricing internals directly) — resolves the gateway client via
/// IPaymentGatewayClientFactory and persists a PaymentTransaction. The captured card is kept (still
/// encrypted) after an authorization so a changed order can be re-authorized without the caller
/// re-keying it; it is wiped at the flow's Commit Point, when the flow session completes, or by the
/// sensitive-data retention job — whichever comes first. See PaymentTransaction/IPaymentGatewayClient for the rest of the design.
/// </summary>
public interface IPaymentService
{
    /// <summary>Loads the call's CallRecord, decrypts SensitiveData (the tf_secure_collect blob),
    /// pulls cardNumber/exp/cvv out of it by the given field-key names (freeform per flow — see
    /// tf_secure_collect's own field config), resolves the amount to authorize (fixedAmount, or the
    /// call's current CallRecord.Cart.CartTotal when null), then authorizes. Card/exp/cvv are always
    /// read from the encrypted blob only — deliberately no variable-template escape hatch for those
    /// (PCI: never normalize "put the card number in an ordinary flow variable" as supported). Zip
    /// is not sensitive, so it supports two sources: <paramref name="zipOverride"/> (an
    /// already-resolved value — pass this when the script has the zip from elsewhere, e.g. an
    /// earlier address node's output variable) takes precedence when non-null; otherwise
    /// <paramref name="zipField"/> is looked up in the same tf_secure_collect blob (for a flow that
    /// captures zip via guided DTMF too). Assigns the call's order number (IOrderNumberService) if
    /// its client has a sequence and sends it to the gateway. Returns an "error" result (no gateway call made) if
    /// there's no captured sensitive data, or a required field key isn't present in it.</summary>
    Task<PaymentAuthResult> AuthorizeAsync(
        Guid callRecordId, string provider,
        string cardNumberField, string expField, string cvvField, string? zipField, string? zipOverride,
        decimal? fixedAmount,
        CancellationToken ct = default);

    /// <summary>Voids the call's most recent approved, not-yet-voided transaction. Fails with no
    /// gateway call at all if there's nothing eligible to void.</summary>
    Task<PaymentVoidResult> VoidMostRecentAsync(Guid callRecordId, CancellationToken ct = default);
}
