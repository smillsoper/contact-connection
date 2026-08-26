using System.Text.Json;
using ContactConnection.Api.Endpoints;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.ApiExecution;
using Moq;
using Xunit;

namespace ContactConnection.Api.Tests.Endpoints;

/// <summary>
/// Covers WebhookReceiveHandler.ProcessAsync — signature verification, dedup, and JSON parsing
/// around the dispatch to CanonicalWebhookMappingEvaluator (covered in its own test class).
/// Replaces the deleted pre-redesign WebhookReceiveHandlerTests, same coverage shape, now driving
/// the standalone canonical-mapping evaluator instead of the old endpoint-scoped fulfillment
/// branch. See API_HARDENING_CHECKLIST.md Tier 2.
/// </summary>
public class WebhookReceiveHandlerTests
{
    private const string Secret = "shh-its-a-secret";

    private static WebhookEndpoint NewWebhook(bool includeTimestamp = false, int toleranceSeconds = 300, object? mappingConfig = null)
    {
        var webhook = WebhookEndpoint.Create("Test Webhook", CanonicalWebhookType.OrderLine);
        webhook.SetSignatureConfig("X-Signature", "SHA256", includeTimestamp, toleranceSeconds);
        if (mappingConfig is not null) webhook.SetMappingConfig(JsonSerializer.Serialize(mappingConfig));
        return webhook;
    }

    private static Mock<IWebhookEventRepository> NewEventRepo(bool exists = false)
    {
        var repo = new Mock<IWebhookEventRepository>();
        repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(exists);
        return repo;
    }

    private static Task<WebhookReceiveHandler.ReceiveResult> Process(
        WebhookEndpoint webhook, string rawBody, string? signatureHeaderValue, string? secret,
        IWebhookEventRepository eventRepo, IOrderRepository? orderRepo = null,
        ICallRecordRepository? callRecordRepo = null, ICustomFieldService? customFieldService = null) =>
        WebhookReceiveHandler.ProcessAsync(
            webhook, Guid.NewGuid(), rawBody, "application/json", signatureHeaderValue, secret,
            eventRepo, orderRepo ?? new Mock<IOrderRepository>().Object,
            callRecordRepo ?? new Mock<ICallRecordRepository>().Object,
            customFieldService ?? new Mock<ICustomFieldService>().Object,
            CancellationToken.None);

    // ── Signature verification ───────────────────────────────────────────────

    [Fact]
    public async Task ValidSignature_NoTimestamp_Accepted()
    {
        var webhook = NewWebhook(includeTimestamp: false);
        const string body = "{}";
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", Secret, body, includeTimestamp: false);
        var eventRepo = NewEventRepo();

        var result = await Process(webhook, body, header, Secret, eventRepo.Object);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.Accepted, result.Outcome);
        Assert.True(result.Event!.SignatureValid);
    }

    [Fact]
    public async Task InvalidSignature_ReturnsInvalidSignatureOutcome_AndPersistsRejectedEvent()
    {
        var webhook = NewWebhook(includeTimestamp: false);
        var eventRepo = NewEventRepo();

        var result = await Process(webhook, "{}", "totally-wrong-signature", Secret, eventRepo.Object);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.InvalidSignature, result.Outcome);
        Assert.Equal(WebhookEventStatus.Rejected, result.Event!.ProcessingStatus);
        eventRepo.Verify(r => r.AddAsync(It.Is<WebhookEvent>(e => e.ProcessingStatus == WebhookEventStatus.Rejected), It.IsAny<CancellationToken>()), Times.Once);
        eventRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NullSecret_TreatedAsInvalidSignature_EvenWithAValidLookingHeader()
    {
        var webhook = NewWebhook(includeTimestamp: false);
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", Secret, "{}", includeTimestamp: false);
        var eventRepo = NewEventRepo();

        // secret: null — e.g. the credential store has no secret stored for this webhook
        var result = await Process(webhook, "{}", header, null, eventRepo.Object);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.InvalidSignature, result.Outcome);
    }

    [Fact]
    public async Task StaleTimestamp_RejectedAsInvalidSignature_EvenWithCorrectHmac()
    {
        var webhook = NewWebhook(includeTimestamp: true, toleranceSeconds: 60);
        const string body = "{}";
        // Hand-build a header with a timestamp far outside tolerance but a correctly computed HMAC for it.
        var staleTimestamp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var signed = HmacSignerTestHelper.ComputeForTimestamp("SHA256", Secret, body, staleTimestamp);
        var header = $"t={staleTimestamp},v1={signed}";
        var eventRepo = NewEventRepo();

        var result = await Process(webhook, body, header, Secret, eventRepo.Object);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.InvalidSignature, result.Outcome);
    }

    // ── Dedup ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DuplicateBody_ReturnsDuplicateOutcome_NoNewEventPersisted()
    {
        var webhook = NewWebhook(includeTimestamp: false);
        const string body = "{}";
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", Secret, body, includeTimestamp: false);
        var eventRepo = NewEventRepo(exists: true); // simulates an already-seen (endpointId, bodyHash)

        var result = await Process(webhook, body, header, Secret, eventRepo.Object);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.Duplicate, result.Outcome);
        Assert.Null(result.Event);
        eventRepo.Verify(r => r.AddAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Body parsing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NonJsonBody_MarksFailed_StillPersistsEvent()
    {
        var webhook = NewWebhook(includeTimestamp: false);
        const string body = "not { json at all";
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", Secret, body, includeTimestamp: false);
        var eventRepo = NewEventRepo();

        var result = await Process(webhook, body, header, Secret, eventRepo.Object);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.Accepted, result.Outcome);
        Assert.Equal(WebhookEventStatus.Failed, result.Event!.ProcessingStatus);
        Assert.Contains("not valid JSON", result.Event.ProcessingError);
    }

    [Fact]
    public async Task EmptyBody_TreatedAsEmptyJsonObject_NotAParseFailure()
    {
        var webhook = NewWebhook(includeTimestamp: false);
        const string body = "";
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", Secret, body, includeTimestamp: false);
        var eventRepo = NewEventRepo();

        var result = await Process(webhook, body, header, Secret, eventRepo.Object);

        // Empty body parses as "{}" — dispatch still runs (and fails for a different reason: no
        // mapping configured), it just isn't a JSON *parse* failure.
        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.Accepted, result.Outcome);
        Assert.DoesNotContain("not valid JSON", result.Event!.ProcessingError ?? "");
    }

    // ── Dispatch outcome propagation ─────────────────────────────────────────

    [Fact]
    public async Task NoMappingConfigured_MarksFailed()
    {
        var webhook = NewWebhook(includeTimestamp: false); // default MappingConfig "{}"
        const string body = "{}";
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", Secret, body, includeTimestamp: false);
        var eventRepo = NewEventRepo();

        var result = await Process(webhook, body, header, Secret, eventRepo.Object);

        Assert.Equal(WebhookEventStatus.Failed, result.Event!.ProcessingStatus);
        Assert.Contains("no mapping configured", result.Event.ProcessingError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulDispatch_MarksProcessed_WithOutcomeSummary()
    {
        var lineId = Guid.NewGuid();
        var webhook = NewWebhook(includeTimestamp: false, mappingConfig: new
        {
            canonicalType = "order_line",
            rootMatch = new { sourcePath = "orderLineId", matchField = "Id" },
            operation = new { name = "MarkDelivered", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });
        var order = Order.CreateFromCart(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ContactConnection.Domain.ValueObjects.Commerce.CartDocument.Empty(),
            [OrderLine.FromCartItem(Guid.NewGuid(), Guid.NewGuid(), new ContactConnection.Domain.ValueObjects.Commerce.CartItem(
                OfferId: Guid.NewGuid(), ProductId: Guid.NewGuid(), Sku: "SKU-A", Description: "x",
                Quantity: 1, FullPrice: 1m, ExtendedPrice: 1m, Shipping: 0m, Weight: 0m, SalesTax: 0m,
                ShippingExempt: true, TaxExempt: true, OnBackOrder: false, AutoShip: false, AutoShipIntervalDays: 0,
                IsUpsell: false, UpsellQty: 0, MixMatchCode: null, ShipMethod: null, DeliveryMessage: null, ShipToJson: null,
                Payments: [], PersonalizationAnswers: [], KitSelections: [],
                CanadaSurcharge: 0m, AKHISurcharge: 0m, OutlyingUSSurcharge: 0m, ForeignSurcharge: 0m))]);
        var actualLineId = order.Lines[0].Id;

        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByLineIdAsync(actualLineId, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var body = $$"""{"orderLineId":"{{actualLineId}}"}""";
        var header = HmacSigner.ComputeSignatureHeaderValue("SHA256", Secret, body, includeTimestamp: false);
        var eventRepo = NewEventRepo();

        var result = await Process(webhook, body, header, Secret, eventRepo.Object, orderRepo: orderRepo.Object);

        Assert.Equal(WebhookEventStatus.Processed, result.Event!.ProcessingStatus);
        Assert.Equal(OrderLineStatus.Delivered, order.Lines[0].FulfillmentStatus);
    }
}

/// <summary>Tiny helper to hand-build a stale-timestamp HMAC header for the replay-window test —
/// HmacSigner.ComputeSignatureHeaderValue always stamps DateTimeOffset.UtcNow, so it can't itself
/// produce a header with an arbitrary past timestamp.</summary>
internal static class HmacSignerTestHelper
{
    public static string ComputeForTimestamp(string algorithm, string secret, string payload, long unixSeconds)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{unixSeconds}.{payload}"));
        return Convert.ToHexStringLower(hash);
    }
}
