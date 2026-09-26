using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.Payments;

/// <summary>
/// See IPaymentService. The orchestrator every node handler calls — owns all CallRecord access for
/// payment operations (mirrors ICartService owning all CallRecord access for cart operations):
/// decrypts/parses the tf_secure_collect SensitiveData blob, resolves the amount to authorize,
/// resolves the gateway client via IPaymentGatewayClientFactory, persists a PaymentTransaction, and
/// wipes CallRecord.SensitiveData on a definitive result.
/// </summary>
public class PaymentService(
    ICallRecordRepository callRecords,
    ISensitiveDataProtector sensitiveData,
    IPaymentGatewayClientFactory gatewayClients,
    IPaymentTransactionRepository transactions) : IPaymentService
{
    public async Task<PaymentAuthResult> AuthorizeAsync(
        Guid callRecordId, string provider,
        string cardNumberField, string expField, string cvvField, string? zipField, string? zipOverride,
        decimal? fixedAmount,
        CancellationToken ct = default)
    {
        var record = await callRecords.GetByIdAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found.");

        if (string.IsNullOrEmpty(record.SensitiveData))
            return new PaymentAuthResult(false, PaymentTransactionStatus.Error, null, null, null,
                "No card data has been captured on this call yet.");

        Dictionary<string, string>? fields;
        try
        {
            var plaintext = sensitiveData.Unprotect(record.SensitiveData);
            fields = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext);
        }
        catch (Exception ex)
        {
            return new PaymentAuthResult(false, PaymentTransactionStatus.Error, null, null, null,
                $"Could not read captured card data: {ex.Message}");
        }

        if (fields is null
            || !fields.TryGetValue(cardNumberField, out var cardNumber)
            || !fields.TryGetValue(expField, out var expirationMMYY)
            || !fields.TryGetValue(cvvField, out var cvv))
            return new PaymentAuthResult(false, PaymentTransactionStatus.Error, null, null, null,
                "Captured card data is missing one or more configured fields.");

        var zip = !string.IsNullOrEmpty(zipOverride) ? zipOverride
            : zipField is not null && fields.TryGetValue(zipField, out var zipValue) ? zipValue : null;
        var amount = fixedAmount ?? record.Cart?.CartTotal ?? 0m;
        if (amount <= 0)
            return new PaymentAuthResult(false, PaymentTransactionStatus.Error, null, null, null,
                "No amount to authorize — cart is empty and no fixed amount was configured.");

        var client = gatewayClients.Resolve(provider);
        var result = await client.AuthorizeAsync(record.CampaignId, record.ClientId, amount, cardNumber, expirationMMYY, cvv, zip, ct);

        var transaction = PaymentTransaction.Create(
            id: Guid.NewGuid(),
            tenantId: record.TenantId,
            callRecordId: callRecordId,
            clientId: record.ClientId,
            campaignId: record.CampaignId,
            gateway: provider,
            amount: amount,
            status: result.Status,
            gatewayTransactionId: result.GatewayTransactionId,
            authCode: result.AuthCode,
            responseCode: result.ResponseCode,
            responseReasonText: result.ResponseReasonText,
            avsResultCode: result.AvsResultCode,
            cvvResultCode: result.CvvResultCode,
            cardLast4: result.CardLast4,
            cardType: result.CardType);

        await transactions.AddAsync(transaction, ct);
        await transactions.SaveChangesAsync(ct);

        // Definitive gateway response (approved or declined) — the card data has served its purpose.
        // On "error" (network/timeout/malformed response — unclear whether the gateway ever saw the
        // card), deliberately leave it in place so a script's retry loop doesn't force the caller
        // through another guided-DTMF capture.
        if (result.Status is PaymentTransactionStatus.Approved or PaymentTransactionStatus.Declined)
        {
            record.WipeSensitiveData("api_processed");
            await callRecords.SaveChangesAsync(ct);
        }

        return new PaymentAuthResult(
            result.Succeeded, result.Status, transaction.Id, result.GatewayTransactionId,
            result.AuthCode, result.ResponseReasonText);
    }

    public async Task<PaymentVoidResult> VoidMostRecentAsync(Guid callRecordId, CancellationToken ct = default)
    {
        var transaction = await transactions.GetMostRecentApprovedAsync(callRecordId, ct);
        if (transaction is null)
            return new PaymentVoidResult(false, "No approved, un-voided transaction found for this call.");

        var client = gatewayClients.Resolve(transaction.Gateway);
        var result = await client.VoidAsync(transaction.CampaignId, transaction.ClientId, transaction.GatewayTransactionId!, ct);

        if (result.Succeeded)
        {
            transaction.MarkVoided();
            await transactions.SaveChangesAsync(ct);
        }

        return new PaymentVoidResult(result.Succeeded, result.ResponseReasonText);
    }
}
