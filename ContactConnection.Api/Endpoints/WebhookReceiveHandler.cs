using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.ApiExecution;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Core inbound-webhook processing logic, factored out of WebhooksEndpoints.cs's minimal-API
/// delegate so it's unit-testable without spinning up the HTTP pipeline — mirrors the
/// ApiEndpointTestHelper precedent. Takes an already-resolved WebhookEndpoint (token lookup +
/// tenant resolution happen in the thin endpoint delegate); this class owns signature
/// verification, dedup, and delegates payload-mapping/dispatch to
/// CanonicalWebhookMappingEvaluator. See API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook
/// support".
/// </summary>
internal static class WebhookReceiveHandler
{
    public enum ReceiveOutcome { Accepted, InvalidSignature, Duplicate }

    public sealed record ReceiveResult(ReceiveOutcome Outcome, WebhookEvent? Event);

    public static async Task<ReceiveResult> ProcessAsync(
        WebhookEndpoint webhookEndpoint,
        Guid tenantId,
        string rawBody,
        string? contentType,
        string? signatureHeaderValue,
        string? secret,
        IWebhookEventRepository eventRepo,
        IOrderRepository orderRepo,
        ICallRecordRepository callRecordRepo,
        ICustomFieldService customFieldService,
        CancellationToken ct)
    {
        var signatureValid = secret is not null && HmacSigner.VerifySignatureHeaderValue(
            webhookEndpoint.SignatureAlgorithm, secret, rawBody, signatureHeaderValue,
            webhookEndpoint.IncludeTimestamp, webhookEndpoint.TimestampToleranceSeconds);

        if (!signatureValid)
        {
            var rejected = WebhookEvent.Create(webhookEndpoint.Id, rawBody, contentType, signatureValid: false);
            rejected.MarkRejected("Signature verification failed.");
            await eventRepo.AddAsync(rejected, ct);
            await eventRepo.SaveChangesAsync(ct);
            return new ReceiveResult(ReceiveOutcome.InvalidSignature, rejected);
        }

        var bodyHash = WebhookEvent.ComputeBodyHash(rawBody);
        if (await eventRepo.ExistsAsync(webhookEndpoint.Id, bodyHash, ct))
        {
            // Exact-duplicate redelivery (a common vendor retry pattern) — no new row (the
            // (WebhookEndpointId, BodyHash) unique index wouldn't allow one anyway), don't
            // reprocess, just tell the caller so it can ack fast.
            return new ReceiveResult(ReceiveOutcome.Duplicate, null);
        }

        var evt = WebhookEvent.Create(webhookEndpoint.Id, rawBody, contentType, signatureValid: true);

        JsonElement body;
        try
        {
            body = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody).RootElement;
        }
        catch
        {
            evt.MarkFailed(null, "Request body is not valid JSON.");
            await eventRepo.AddAsync(evt, ct);
            await eventRepo.SaveChangesAsync(ct);
            return new ReceiveResult(ReceiveOutcome.Accepted, evt);
        }

        try
        {
            var result = await CanonicalWebhookMappingEvaluator.EvaluateAndDispatchAsync(
                webhookEndpoint, body, orderRepo, callRecordRepo, customFieldService, tenantId, ct);

            if (result.Applied) evt.MarkProcessed(result.OutcomeSummary);
            else evt.MarkFailed(result.OutcomeSummary, result.Error ?? result.OutcomeSummary);
        }
        catch (Exception ex)
        {
            evt.MarkFailed(null, ex.Message);
        }

        await eventRepo.AddAsync(evt, ct);
        await eventRepo.SaveChangesAsync(ct);
        return new ReceiveResult(ReceiveOutcome.Accepted, evt);
    }
}
