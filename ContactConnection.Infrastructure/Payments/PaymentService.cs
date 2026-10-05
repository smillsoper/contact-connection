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
    IPaymentTransactionRepository transactions,
    IOrderNumberService orderNumbers) : IPaymentService
{
    public async Task<PaymentAuthResult> AuthorizeAsync(
        Guid callRecordId, string provider,
        string cardNumberField, string expField, string cvvField, string? zipField, string? zipOverride,
        decimal? fixedAmount,
        CancellationToken ct = default,
        Guid? interactionId = null)
    {
        var record = await callRecords.GetByIdWithInteractionsAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found.");

        // Interaction-scoped (S178): this interaction's cart, its own authorization, its campaign's gateway
        // credentials, its order number. A legacy record with no interactions behaves as before.
        var ix = record.CommerceInteraction(interactionId);
        var cart = ix is null ? record.Cart : ix.Cart;
        var campaignId = ix?.CampaignId ?? record.CampaignId;
        var amount = fixedAmount ?? cart?.CartTotal ?? 0m;
        if (amount <= 0)
            return new PaymentAuthResult(false, PaymentTransactionStatus.Error, null, null, null,
                "No amount to authorize — cart is empty and no fixed amount was configured.");

        // The node can be reached again after an order change. A live authorization for this exact
        // amount on the same card capture needs nothing; anything else is voided first so the caller
        // is never holding two authorizations.
        var action = PaymentAuthAction.Authorized;
        var existing = await transactions.GetMostRecentApprovedAsync(callRecordId, ix?.Id, ct);
        if (existing is not null)
        {
            var cardRecaptured = record.SensitiveDataStoredAt is { } storedAt && storedAt > existing.CreatedAt;
            if (!cardRecaptured && existing.Amount == amount)
                return new PaymentAuthResult(true, PaymentTransactionStatus.Approved, existing.Id,
                    existing.GatewayTransactionId, existing.AuthCode, "Already authorized for this amount.",
                    existing.OrderNumber, PaymentAuthAction.AlreadyAuthorized, existing.Amount, existing.CardLast4);

            var voidResult = await gatewayClients.Resolve(existing.Gateway)
                .VoidAsync(existing.CampaignId, existing.ClientId, existing.GatewayTransactionId!, ct);
            if (!voidResult.Succeeded)
                return new PaymentAuthResult(false, PaymentTransactionStatus.Error, existing.Id,
                    existing.GatewayTransactionId, existing.AuthCode,
                    $"Could not void the previous authorization ({voidResult.ResponseReasonText}) — not re-authorizing, " +
                    "so the caller's card isn't held twice.",
                    existing.OrderNumber, PaymentAuthAction.Reauthorized, existing.Amount, existing.CardLast4);
            existing.MarkVoided();
            await transactions.SaveChangesAsync(ct);
            action = PaymentAuthAction.Reauthorized;
        }

        if (string.IsNullOrEmpty(record.SensitiveData))
            return new PaymentAuthResult(false, PaymentTransactionStatus.Error, null, null, null,
                action == PaymentAuthAction.Reauthorized
                    ? "The previous authorization was voided, but the card is no longer on file — capture the card again."
                    : "No card data has been captured on this call yet.",
                Action: action);

        Dictionary<string, string>? fields;
        try
        {
            var plaintext = sensitiveData.Unprotect(record.SensitiveData);
            fields = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext);
        }
        catch (Exception ex)
        {
            return new PaymentAuthResult(false, PaymentTransactionStatus.Error, null, null, null,
                $"Could not read captured card data: {ex.Message}", Action: action);
        }

        if (fields is null
            || !fields.TryGetValue(cardNumberField, out var cardNumber)
            || !fields.TryGetValue(expField, out var expirationMMYY)
            || !fields.TryGetValue(cvvField, out var cvv))
            return new PaymentAuthResult(false, PaymentTransactionStatus.Error, null, null, null,
                "Captured card data is missing one or more configured fields.", Action: action);

        var zip = !string.IsNullOrEmpty(zipOverride) ? zipOverride
            : zipField is not null && fields.TryGetValue(zipField, out var zipValue) ? zipValue : null;

        var client = gatewayClients.Resolve(provider);
        var orderNumber = await orderNumbers.GetOrAssignAsync(record, ix, ct);
        var result = await client.AuthorizeAsync(
            campaignId, record.ClientId, amount, cardNumber, expirationMMYY, cvv, zip, orderNumber, ct);

        var transaction = PaymentTransaction.Create(
            id: Guid.NewGuid(),
            tenantId: record.TenantId,
            callRecordId: callRecordId,
            clientId: record.ClientId,
            campaignId: campaignId,
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
            cardType: result.CardType,
            orderNumber: orderNumber);

        if (ix is not null) transaction.SetInteraction(ix.Id);
        await transactions.AddAsync(transaction, ct);
        await transactions.SaveChangesAsync(ct);

        // The card is deliberately NOT wiped here any more (S164): a changed order re-authorizes, and a
        // decline after fixing the billing address retries, without the caller re-keying it. It's wiped
        // at the Commit Point, when the flow session completes, or by the retention job.

        return new PaymentAuthResult(
            result.Succeeded, result.Status, transaction.Id, result.GatewayTransactionId,
            result.AuthCode, result.ResponseReasonText, orderNumber, action, amount, result.CardLast4);
    }

    public async Task<PaymentVoidResult> VoidMostRecentAsync(Guid callRecordId, CancellationToken ct = default, Guid? interactionId = null)
    {
        var transaction = await transactions.GetMostRecentApprovedAsync(callRecordId, interactionId, ct);
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
