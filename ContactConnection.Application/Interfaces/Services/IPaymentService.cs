namespace ContactConnection.Application.Interfaces.Services;

public record PaymentAuthResult(
    bool Succeeded,
    string Status, // PaymentTransactionStatus: approved | declined | error
    Guid? TransactionId,
    string? GatewayTransactionId,
    string? AuthCode,
    string? ResponseReasonText);

public record PaymentVoidResult(bool Succeeded, string? ResponseReasonText);

/// <summary>
/// The orchestrator every node handler calls (mirrors how node handlers call ICartService, never
/// touch inventory/pricing internals directly) — resolves the gateway client via
/// IPaymentGatewayClientFactory, persists a PaymentTransaction, and wipes CallRecord.SensitiveData
/// on a definitive result. See PaymentTransaction/IPaymentGatewayClient for the rest of the design.
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
    /// captures zip via guided DTMF too). Returns an "error" result (no gateway call made) if
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
