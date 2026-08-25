using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.ApiExecution;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Core inbound-webhook processing logic, factored out of WebhooksEndpoints.cs's minimal-API
/// delegate so it's unit-testable without spinning up the HTTP pipeline — mirrors the
/// ApiEndpointTestHelper precedent. Takes already-resolved WebhookEndpoint/TenantApiEndpoint
/// (token lookup + tenant resolution happen in the thin endpoint delegate); this class owns
/// signature verification, dedup, payload-mapping evaluation, and dispatch. See
/// API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support".
/// </summary>
internal static class WebhookReceiveHandler
{
    public enum ReceiveOutcome { Accepted, InvalidSignature, Duplicate }

    public sealed record ReceiveResult(ReceiveOutcome Outcome, WebhookEvent? Event);

    public static async Task<ReceiveResult> ProcessAsync(
        WebhookEndpoint webhookEndpoint,
        TenantApiEndpoint apiEndpoint,
        string rawBody,
        string? contentType,
        string? signatureHeaderValue,
        string? secret,
        IWebhookEventRepository eventRepo,
        IOrderRepository orderRepo,
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

        // Reused as-is despite the "Address" name — its return shape (outcome name + resolved
        // to-target dictionary) is domain-agnostic. See API_HARDENING_CHECKLIST.md Tier 2 note.
        var mapped = AddressResponseMappingEvaluator.Evaluate(apiEndpoint.ResponseMapping, body);

        try
        {
            if (apiEndpoint.ApiSubType == ApiSubType.FulfillmentTracking)
                await DispatchFulfillmentTrackingAsync(evt, mapped, orderRepo, ct);
            else
                evt.SetOutcomeKey(mapped.OutcomeKey); // no dispatch target for this sub-type yet — stored/logged only
        }
        catch (Exception ex)
        {
            evt.MarkFailed(mapped.OutcomeKey, ex.Message);
        }

        await eventRepo.AddAsync(evt, ct);
        await eventRepo.SaveChangesAsync(ct);
        return new ReceiveResult(ReceiveOutcome.Accepted, evt);
    }

    /// <summary>
    /// Dispatches a fulfillment_tracking webhook. Reserved outcome names, matching how the
    /// admin configures the endpoint's ResponseMapping outcomes:
    ///   "shipped"   → requires to-targets "orderLineId" + "trackingNumber" → OrderLine.Ship(...)
    ///   "delivered" → requires to-target "orderLineId" → OrderLine.MarkDelivered()
    /// Any other/unrecognized outcome name is stored only (evt stays "received").
    /// </summary>
    private static async Task DispatchFulfillmentTrackingAsync(
        WebhookEvent evt, AddressValidationResult mapped, IOrderRepository orderRepo, CancellationToken ct)
    {
        var outcome = mapped.OutcomeKey;
        if (outcome != "shipped" && outcome != "delivered")
        {
            evt.SetOutcomeKey(outcome);
            return;
        }

        var fields = mapped.CorrectedFields ?? new Dictionary<string, string>();
        if (!fields.TryGetValue("orderLineId", out var orderLineIdStr) || !Guid.TryParse(orderLineIdStr, out var orderLineId))
        {
            evt.MarkFailed(outcome, "Payload mapping did not resolve a valid 'orderLineId' field.");
            return;
        }

        var order = await orderRepo.GetByLineIdAsync(orderLineId, ct);
        var line = order?.Lines.FirstOrDefault(l => l.Id == orderLineId);
        if (order is null || line is null)
        {
            evt.MarkFailed(outcome, $"No order line found for id '{orderLineId}'.");
            return;
        }

        if (outcome == "shipped")
        {
            if (!fields.TryGetValue("trackingNumber", out var trackingNumber) || string.IsNullOrWhiteSpace(trackingNumber))
            {
                evt.MarkFailed(outcome, "Payload mapping did not resolve a 'trackingNumber' field.");
                return;
            }
            line.Ship(trackingNumber);
        }
        else
        {
            line.MarkDelivered();
        }

        order.RefreshStatus();
        await orderRepo.SaveChangesAsync(ct);
        evt.MarkProcessed(outcome);
    }
}
