using ContactConnection.Api.Endpoints;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;
using ContactConnection.Infrastructure.ApiExecution;
using Moq;
using Xunit;

namespace ContactConnection.Api.Tests.Endpoints;

/// <summary>
/// Covers WebhookReceiveHandler — the core inbound-webhook processing logic (signature
/// verification, dedup, payload-mapping evaluation, fulfillment_tracking dispatch) behind
/// WebhooksEndpoints.cs's public receiver. See API_HARDENING_CHECKLIST.md Tier 2, "Inbound
/// webhook support".
/// </summary>
public class WebhookReceiveHandlerTests
{
    private const string Secret = "test-secret";

    // Outcomes "shipped"/"delivered" keyed on a "status" field, mapping "line_id"/"tracking" from
    // the payload into the reserved "orderLineId"/"trackingNumber" to-targets the dispatcher
    // reads — the exact DSL an admin would configure via ResponseMappingPanel.
    private const string FulfillmentMapping = """
        {"outcomes":[
          {"name":"shipped","conditions":[{"path":"status","op":"eq","value":"shipped"}],
           "fieldMappings":[{"from":"line_id","to":"orderLineId"},{"from":"tracking","to":"trackingNumber"}]},
          {"name":"delivered","conditions":[{"path":"status","op":"eq","value":"delivered"}],
           "fieldMappings":[{"from":"line_id","to":"orderLineId"}]}
        ]}
        """;

    private static WebhookEndpoint NewWebhookEndpoint()
    {
        var endpoint = WebhookEndpoint.Create(Guid.NewGuid());
        // Default to the bare-hex format for most tests — the timestamped-format tests configure
        // IncludeTimestamp explicitly.
        endpoint.SetSignatureConfig("X-Signature", "SHA256", includeTimestamp: false, toleranceSeconds: 300);
        return endpoint;
    }

    private static TenantApiEndpoint NewFulfillmentTrackingEndpoint(string? responseMapping = null)
    {
        var endpoint = TenantApiEndpoint.Create(
            Guid.NewGuid(), ApiCategory.Fulfillment, ApiSubType.FulfillmentTracking, "Tracking Webhook", "/webhook");
        endpoint.SetResponseMapping(responseMapping ?? FulfillmentMapping);
        return endpoint;
    }

    private static TenantApiEndpoint NewUnwiredSubTypeEndpoint() =>
        TenantApiEndpoint.Create(Guid.NewGuid(), ApiCategory.Media, ApiSubType.CampaignResults, "Campaign Results", "/webhook");

    private static CartItem MakeCartItem() => new(
        OfferId: Guid.NewGuid(), ProductId: Guid.NewGuid(), Sku: "SKU001", Description: "Test Product",
        Quantity: 1, FullPrice: 29.95m, ExtendedPrice: 29.95m, Shipping: 5.95m, Weight: 1.0m, SalesTax: 0m,
        ShippingExempt: false, TaxExempt: false, OnBackOrder: false, AutoShip: false, AutoShipIntervalDays: 0,
        IsUpsell: false, UpsellQty: 0, MixMatchCode: null, ShipMethod: null, DeliveryMessage: null, ShipToJson: null,
        Payments: [], PersonalizationAnswers: [], KitSelections: [],
        CanadaSurcharge: 0m, AKHISurcharge: 0m, OutlyingUSSurcharge: 0m, ForeignSurcharge: 0m);

    private static (Order order, OrderLine line) MakeOrderWithLine()
    {
        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var line = OrderLine.FromCartItem(orderId, tenantId, MakeCartItem());
        var order = Order.CreateFromCart(orderId, tenantId, null, CartDocument.Empty(), [line]);
        return (order, line);
    }

    private static Mock<IWebhookEventRepository> MockEventRepo(bool exists = false)
    {
        var mock = new Mock<IWebhookEventRepository>();
        mock.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists);
        return mock;
    }

    // ── Signature verification ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_ValidSignature_ReturnsAccepted_AndLogsSignatureValidTrue()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        var apiEndpoint = NewUnwiredSubTypeEndpoint();
        var body = "{\"hello\":\"world\"}";
        var header = HmacSigner.ComputeSignatureHeaderValue(webhookEndpoint.SignatureAlgorithm, Secret, body, includeTimestamp: false);
        var eventRepo = MockEventRepo();
        var orderRepo = new Mock<IOrderRepository>();

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, body, "application/json", header, Secret, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.Accepted, result.Outcome);
        Assert.NotNull(result.Event);
        Assert.True(result.Event!.SignatureValid);
        eventRepo.Verify(r => r.AddAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        eventRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_InvalidSignature_ReturnsInvalidSignature_LogsRejectedEvent_NoDispatch()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        var apiEndpoint = NewFulfillmentTrackingEndpoint();
        var eventRepo = MockEventRepo();
        var orderRepo = new Mock<IOrderRepository>();

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, "{\"status\":\"shipped\"}", "application/json",
            "not-a-real-signature", Secret, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.InvalidSignature, result.Outcome);
        Assert.NotNull(result.Event);
        Assert.False(result.Event!.SignatureValid);
        Assert.Equal(WebhookEventStatus.Rejected, result.Event.ProcessingStatus);
        orderRepo.Verify(r => r.GetByLineIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_NullSecret_TreatedAsInvalidSignature()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        var apiEndpoint = NewUnwiredSubTypeEndpoint();
        var eventRepo = MockEventRepo();
        var orderRepo = new Mock<IOrderRepository>();

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, "{}", null, "anything", secret: null, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.InvalidSignature, result.Outcome);
    }

    [Fact]
    public async Task ProcessAsync_TimestampOutsideTolerance_TreatedAsInvalidSignature()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        webhookEndpoint.SetSignatureConfig("X-Signature", "SHA256", includeTimestamp: true, toleranceSeconds: 60);
        var apiEndpoint = NewUnwiredSubTypeEndpoint();
        var body = "{}";
        // Compute a correctly-signed header, but stamped far enough in the past to fall outside
        // the 60s tolerance — a valid signature over a stale timestamp is still rejected (replay
        // protection).
        var staleHeader = ComputeStaleTimestampedHeader(webhookEndpoint.SignatureAlgorithm, Secret, body, secondsAgo: 600);
        var eventRepo = MockEventRepo();
        var orderRepo = new Mock<IOrderRepository>();

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, body, "application/json", staleHeader, Secret, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.InvalidSignature, result.Outcome);
    }

    private static string ComputeStaleTimestampedHeader(string algorithm, string secret, string payload, int secondsAgo)
    {
        var timestamp = DateTimeOffset.UtcNow.AddSeconds(-secondsAgo).ToUnixTimeSeconds();
        // Recompute the same way HmacSigner does internally, just with a caller-chosen timestamp.
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = Convert.ToHexStringLower(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{timestamp}.{payload}")));
        return $"t={timestamp},v1={hash}";
    }

    // ── Dedup ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_DuplicateBody_ReturnsDuplicate_DoesNotInsertOrDispatch()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        var apiEndpoint = NewFulfillmentTrackingEndpoint();
        var body = "{\"status\":\"shipped\",\"line_id\":\"" + Guid.NewGuid() + "\",\"tracking\":\"1Z999\"}";
        var header = HmacSigner.ComputeSignatureHeaderValue(webhookEndpoint.SignatureAlgorithm, Secret, body, includeTimestamp: false);
        var eventRepo = MockEventRepo(exists: true);
        var orderRepo = new Mock<IOrderRepository>();

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, body, "application/json", header, Secret, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.Duplicate, result.Outcome);
        Assert.Null(result.Event);
        eventRepo.Verify(r => r.AddAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        orderRepo.Verify(r => r.GetByLineIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Non-JSON body ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_NonJsonBody_MarksFailed_ButStillAccepted()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        var apiEndpoint = NewFulfillmentTrackingEndpoint();
        var body = "not json at all";
        var header = HmacSigner.ComputeSignatureHeaderValue(webhookEndpoint.SignatureAlgorithm, Secret, body, includeTimestamp: false);
        var eventRepo = MockEventRepo();
        var orderRepo = new Mock<IOrderRepository>();

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, body, "text/plain", header, Secret, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        Assert.Equal(WebhookReceiveHandler.ReceiveOutcome.Accepted, result.Outcome);
        Assert.Equal(WebhookEventStatus.Failed, result.Event!.ProcessingStatus);
        Assert.Contains("not valid JSON", result.Event.ProcessingError);
    }

    // ── fulfillment_tracking dispatch ───────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_FulfillmentTracking_Shipped_CallsShip_AndMarksProcessed()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        var apiEndpoint = NewFulfillmentTrackingEndpoint();
        var (order, line) = MakeOrderWithLine();
        var body = $$"""{"status":"shipped","line_id":"{{line.Id}}","tracking":"1Z999AA10123456784"}""";
        var header = HmacSigner.ComputeSignatureHeaderValue(webhookEndpoint.SignatureAlgorithm, Secret, body, includeTimestamp: false);
        var eventRepo = MockEventRepo();
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByLineIdAsync(line.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, body, "application/json", header, Secret, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        Assert.Equal(WebhookEventStatus.Processed, result.Event!.ProcessingStatus);
        Assert.Equal("shipped", result.Event.OutcomeKey);
        Assert.Equal(OrderLineStatus.Shipped, line.FulfillmentStatus);
        Assert.Equal("1Z999AA10123456784", line.TrackingNumber);
        orderRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_FulfillmentTracking_Delivered_CallsMarkDelivered_AndMarksProcessed()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        var apiEndpoint = NewFulfillmentTrackingEndpoint();
        var (order, line) = MakeOrderWithLine();
        line.Ship("1Z999AA10123456784"); // deliveries follow a prior ship in the real world
        var body = $$"""{"status":"delivered","line_id":"{{line.Id}}"}""";
        var header = HmacSigner.ComputeSignatureHeaderValue(webhookEndpoint.SignatureAlgorithm, Secret, body, includeTimestamp: false);
        var eventRepo = MockEventRepo();
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByLineIdAsync(line.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, body, "application/json", header, Secret, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        Assert.Equal(WebhookEventStatus.Processed, result.Event!.ProcessingStatus);
        Assert.Equal(OrderLineStatus.Delivered, line.FulfillmentStatus);
    }

    [Fact]
    public async Task ProcessAsync_FulfillmentTracking_ShippedMissingTrackingNumber_MarksFailed()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        var apiEndpoint = NewFulfillmentTrackingEndpoint();
        var (order, line) = MakeOrderWithLine();
        // "tracking" field omitted from the payload entirely — fieldMapping resolves nothing for it.
        var body = $$"""{"status":"shipped","line_id":"{{line.Id}}"}""";
        var header = HmacSigner.ComputeSignatureHeaderValue(webhookEndpoint.SignatureAlgorithm, Secret, body, includeTimestamp: false);
        var eventRepo = MockEventRepo();
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByLineIdAsync(line.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, body, "application/json", header, Secret, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        Assert.Equal(WebhookEventStatus.Failed, result.Event!.ProcessingStatus);
        Assert.Contains("trackingNumber", result.Event.ProcessingError);
        Assert.Equal(OrderLineStatus.Pending, line.FulfillmentStatus); // untouched
        orderRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_FulfillmentTracking_NoMatchingOrderLine_MarksFailed()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        var apiEndpoint = NewFulfillmentTrackingEndpoint();
        var unknownLineId = Guid.NewGuid();
        var body = $$"""{"status":"shipped","line_id":"{{unknownLineId}}","tracking":"1Z999"}""";
        var header = HmacSigner.ComputeSignatureHeaderValue(webhookEndpoint.SignatureAlgorithm, Secret, body, includeTimestamp: false);
        var eventRepo = MockEventRepo();
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByLineIdAsync(unknownLineId, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, body, "application/json", header, Secret, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        Assert.Equal(WebhookEventStatus.Failed, result.Event!.ProcessingStatus);
        Assert.Contains("No order line found", result.Event.ProcessingError);
    }

    [Fact]
    public async Task ProcessAsync_UnrecognizedSubType_StoresOnly_DoesNotDispatch()
    {
        var webhookEndpoint = NewWebhookEndpoint();
        var apiEndpoint = NewUnwiredSubTypeEndpoint(); // campaign_results — no domain sink yet
        apiEndpoint.SetResponseMapping(FulfillmentMapping); // even if an outcome would match...
        var body = "{\"status\":\"shipped\",\"line_id\":\"" + Guid.NewGuid() + "\",\"tracking\":\"1Z999\"}";
        var header = HmacSigner.ComputeSignatureHeaderValue(webhookEndpoint.SignatureAlgorithm, Secret, body, includeTimestamp: false);
        var eventRepo = MockEventRepo();
        var orderRepo = new Mock<IOrderRepository>();

        var result = await WebhookReceiveHandler.ProcessAsync(
            webhookEndpoint, apiEndpoint, body, "application/json", header, Secret, eventRepo.Object, orderRepo.Object, CancellationToken.None);

        // ...it's never dispatched, because this sub-type has no wired consumer.
        Assert.Equal(WebhookEventStatus.Received, result.Event!.ProcessingStatus);
        Assert.Equal("shipped", result.Event.OutcomeKey); // still recorded for visibility
        orderRepo.Verify(r => r.GetByLineIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
