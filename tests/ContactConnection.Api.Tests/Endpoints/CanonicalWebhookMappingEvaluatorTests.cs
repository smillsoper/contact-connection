using System.Text.Json;
using ContactConnection.Api.Endpoints;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;
using Moq;
using Xunit;

namespace ContactConnection.Api.Tests.Endpoints;

/// <summary>
/// Covers CanonicalWebhookMappingEvaluator — the core match+dispatch logic that replaced the
/// original endpoint-scoped fulfillment-tracking design (Session 90 pre-merge redesign). Exercises
/// all three canonical types (order_line flat, order flat, order+itemsArray, call_record), both
/// no-match policies, and both multiple-match policies for the array shape. See
/// API_HARDENING_CHECKLIST.md Tier 2, "Inbound webhook support".
/// </summary>
public class CanonicalWebhookMappingEvaluatorTests
{
    private static WebhookEndpoint NewWebhook(string canonicalType, object mappingConfig)
    {
        var webhook = WebhookEndpoint.Create("Test Webhook", canonicalType);
        webhook.SetMappingConfig(JsonSerializer.Serialize(mappingConfig));
        return webhook;
    }

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    private static CartItem MakeCartItem(string sku) => new(
        OfferId: Guid.NewGuid(), ProductId: Guid.NewGuid(), Sku: sku, Description: "Test Product",
        Quantity: 1, FullPrice: 29.95m, ExtendedPrice: 29.95m, Shipping: 5.95m, Weight: 1.0m, SalesTax: 0m,
        ShippingExempt: false, TaxExempt: false, OnBackOrder: false, AutoShip: false, AutoShipIntervalDays: 0,
        IsUpsell: false, UpsellQty: 0, MixMatchCode: null, ShipMethod: null, DeliveryMessage: null, ShipToJson: null,
        Payments: [], PersonalizationAnswers: [], KitSelections: [],
        CanadaSurcharge: 0m, AKHISurcharge: 0m, OutlyingUSSurcharge: 0m, ForeignSurcharge: 0m);

    private static Order MakeOrder(Guid tenantId, params string[] skus)
    {
        var orderId = Guid.NewGuid();
        var lines = skus.Select(sku => OrderLine.FromCartItem(orderId, tenantId, MakeCartItem(sku))).ToList();
        return Order.CreateFromCart(orderId, tenantId, Guid.NewGuid(), CartDocument.Empty(), lines);
    }

    private static Mock<IOrderRepository> NewOrderRepo() => new();
    private static Mock<ICallRecordRepository> NewCallRecordRepo() => new();
    private static Mock<ICustomFieldService> NewCustomFieldService() => new();

    private static Task<WebhookEvaluationResult> Evaluate(
        WebhookEndpoint webhook, JsonElement body,
        IOrderRepository? orderRepo = null, ICallRecordRepository? callRecordRepo = null, ICustomFieldService? customFieldService = null) =>
        CanonicalWebhookMappingEvaluator.EvaluateAndDispatchAsync(
            webhook, body,
            orderRepo ?? NewOrderRepo().Object,
            callRecordRepo ?? NewCallRecordRepo().Object,
            customFieldService ?? NewCustomFieldService().Object,
            webhook.Id, // tenantId unused by dispatch itself; any Guid is fine here
            CancellationToken.None);

    // ── order_line (flat) ────────────────────────────────────────────────────

    [Fact]
    public async Task OrderLine_Ship_MatchedById_UpdatesLineAndRefreshesOrderStatus()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-A");
        var line = order.Lines[0];

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByLineIdAsync(line.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.OrderLine, new
        {
            canonicalType = "order_line",
            rootMatch = new { sourcePath = "orderLineId", matchField = "Id" },
            operation = new { name = "Ship", @params = new Dictionary<string, string> { ["trackingNumber"] = "tracking" } },
            onNoMatch = "skip_and_log",
        });

        var body = Body($$"""{"orderLineId":"{{line.Id}}","tracking":"1Z999"}""");

        var result = await Evaluate(webhook, body, orderRepo: orderRepo.Object);

        Assert.True(result.Applied);
        Assert.Equal(OrderLineStatus.Shipped, line.FulfillmentStatus);
        Assert.Equal("1Z999", line.TrackingNumber);
        Assert.Equal(OrderStatus.Shipped, order.Status); // RefreshStatus called
        orderRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrderLine_MarkDelivered_NoParams_Works()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-A");
        var line = order.Lines[0];

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByLineIdAsync(line.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.OrderLine, new
        {
            canonicalType = "order_line",
            rootMatch = new { sourcePath = "orderLineId", matchField = "Id" },
            operation = new { name = "MarkDelivered", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });

        var result = await Evaluate(webhook, Body($$"""{"orderLineId":"{{line.Id}}"}"""), orderRepo: orderRepo.Object);

        Assert.True(result.Applied);
        Assert.Equal(OrderLineStatus.Delivered, line.FulfillmentStatus);
    }

    [Fact]
    public async Task OrderLine_Cancel_Works()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-A");
        var line = order.Lines[0];

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByLineIdAsync(line.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.OrderLine, new
        {
            canonicalType = "order_line",
            rootMatch = new { sourcePath = "orderLineId", matchField = "Id" },
            operation = new { name = "Cancel", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });

        var result = await Evaluate(webhook, Body($$"""{"orderLineId":"{{line.Id}}"}"""), orderRepo: orderRepo.Object);

        Assert.True(result.Applied);
        Assert.Equal(OrderLineStatus.Cancelled, line.FulfillmentStatus);
    }

    [Fact]
    public async Task OrderLine_NoMatch_SkipAndLog_ReturnsFailure()
    {
        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByLineIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        var webhook = NewWebhook(CanonicalWebhookType.OrderLine, new
        {
            canonicalType = "order_line",
            rootMatch = new { sourcePath = "orderLineId", matchField = "Id" },
            operation = new { name = "MarkDelivered", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });

        var result = await Evaluate(webhook, Body($$"""{"orderLineId":"{{Guid.NewGuid()}}"}"""), orderRepo: orderRepo.Object);

        Assert.False(result.Applied);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task OrderLine_NoMatch_Ignore_ReturnsAppliedTrue_NoError()
    {
        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByLineIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        var webhook = NewWebhook(CanonicalWebhookType.OrderLine, new
        {
            canonicalType = "order_line",
            rootMatch = new { sourcePath = "orderLineId", matchField = "Id" },
            operation = new { name = "MarkDelivered", @params = new Dictionary<string, string>() },
            onNoMatch = "ignore",
        });

        var result = await Evaluate(webhook, Body($$"""{"orderLineId":"{{Guid.NewGuid()}}"}"""), orderRepo: orderRepo.Object);

        Assert.True(result.Applied);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task OrderLine_Ship_MissingTrackingParam_Fails()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-A");
        var line = order.Lines[0];

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByLineIdAsync(line.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.OrderLine, new
        {
            canonicalType = "order_line",
            rootMatch = new { sourcePath = "orderLineId", matchField = "Id" },
            operation = new { name = "Ship", @params = new Dictionary<string, string>() }, // no trackingNumber param
            onNoMatch = "skip_and_log",
        });

        var result = await Evaluate(webhook, Body($$"""{"orderLineId":"{{line.Id}}"}"""), orderRepo: orderRepo.Object);

        Assert.False(result.Applied);
        Assert.Equal(OrderLineStatus.Pending, line.FulfillmentStatus); // untouched
    }

    // ── order (flat) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Order_Cancel_MatchedById_Works()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-A");

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.Order, new
        {
            canonicalType = "order",
            rootMatch = new { sourcePath = "orderId", matchField = "Id" },
            itemsArray = (object?)null,
            operation = new { name = "Cancel", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });

        var result = await Evaluate(webhook, Body($$"""{"orderId":"{{order.Id}}"}"""), orderRepo: orderRepo.Object);

        Assert.True(result.Applied);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task Order_MatchedByCallRecordId_Works()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-A");
        var callRecordId = Guid.NewGuid();

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByCallRecordIdAsync(callRecordId, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.Order, new
        {
            canonicalType = "order",
            rootMatch = new { sourcePath = "callRecordId", matchField = "CallRecordId" },
            itemsArray = (object?)null,
            operation = new { name = "Cancel", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });

        var result = await Evaluate(webhook, Body($$"""{"callRecordId":"{{callRecordId}}"}"""), orderRepo: orderRepo.Object);

        Assert.True(result.Applied);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    // ── order + itemsArray (parent+children shape) ───────────────────────────

    [Fact]
    public async Task OrderItemsArray_MatchBySku_UpdatesEachMatchedLine()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-A", "SKU-B");

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.Order, new
        {
            canonicalType = "order",
            rootMatch = new { sourcePath = "orderId", matchField = "Id" },
            itemsArray = new
            {
                arrayPath = "items",
                itemMatch = new { sourcePath = "sku", matchField = "Sku" },
                onNoMatch = "skip_and_log",
                onMultipleMatches = "skip_and_log",
            },
            operation = new { name = "Ship", @params = new Dictionary<string, string> { ["trackingNumber"] = "tracking" } },
            onNoMatch = "skip_and_log",
        });

        var body = Body($$"""
        {
          "orderId": "{{order.Id}}",
          "items": [
            { "sku": "SKU-A", "tracking": "TRACK-A" },
            { "sku": "SKU-B", "tracking": "TRACK-B" }
          ]
        }
        """);

        var result = await Evaluate(webhook, body, orderRepo: orderRepo.Object);

        Assert.True(result.Applied);
        Assert.Equal("TRACK-A", order.Lines.Single(l => l.Sku == "SKU-A").TrackingNumber);
        Assert.Equal("TRACK-B", order.Lines.Single(l => l.Sku == "SKU-B").TrackingNumber);
        Assert.Equal(2, result.ItemResults.Count(r => r.Applied));
    }

    [Fact]
    public async Task OrderItemsArray_MultipleLinesShareSku_SkipAndLog_LeavesBothUntouched()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-DUP", "SKU-DUP"); // two lines share a SKU (unindexed snapshot string)

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.Order, new
        {
            canonicalType = "order",
            rootMatch = new { sourcePath = "orderId", matchField = "Id" },
            itemsArray = new
            {
                arrayPath = "items",
                itemMatch = new { sourcePath = "sku", matchField = "Sku" },
                onNoMatch = "skip_and_log",
                onMultipleMatches = "skip_and_log",
            },
            operation = new { name = "MarkDelivered", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });

        var body = Body($$"""{"orderId":"{{order.Id}}","items":[{"sku":"SKU-DUP"}]}""");

        var result = await Evaluate(webhook, body, orderRepo: orderRepo.Object);

        Assert.False(result.Applied); // nothing was updated
        Assert.All(order.Lines, l => Assert.Equal(OrderLineStatus.Pending, l.FulfillmentStatus));
        var itemResult = Assert.Single(result.ItemResults);
        Assert.False(itemResult.Applied);
        Assert.Contains("ambiguous", itemResult.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OrderItemsArray_MultipleLinesShareSku_UpdateAll_UpdatesBoth()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-DUP", "SKU-DUP");

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.Order, new
        {
            canonicalType = "order",
            rootMatch = new { sourcePath = "orderId", matchField = "Id" },
            itemsArray = new
            {
                arrayPath = "items",
                itemMatch = new { sourcePath = "sku", matchField = "Sku" },
                onNoMatch = "skip_and_log",
                onMultipleMatches = "update_all",
            },
            operation = new { name = "MarkDelivered", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });

        var body = Body($$"""{"orderId":"{{order.Id}}","items":[{"sku":"SKU-DUP"}]}""");

        var result = await Evaluate(webhook, body, orderRepo: orderRepo.Object);

        Assert.True(result.Applied);
        Assert.All(order.Lines, l => Assert.Equal(OrderLineStatus.Delivered, l.FulfillmentStatus));
    }

    [Fact]
    public async Task OrderItemsArray_NoLineMatches_SkipAndLog_LogsError()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-A");

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.Order, new
        {
            canonicalType = "order",
            rootMatch = new { sourcePath = "orderId", matchField = "Id" },
            itemsArray = new
            {
                arrayPath = "items",
                itemMatch = new { sourcePath = "sku", matchField = "Sku" },
                onNoMatch = "skip_and_log",
                onMultipleMatches = "skip_and_log",
            },
            operation = new { name = "MarkDelivered", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });

        var body = Body($$"""{"orderId":"{{order.Id}}","items":[{"sku":"SKU-NONEXISTENT"}]}""");

        var result = await Evaluate(webhook, body, orderRepo: orderRepo.Object);

        Assert.False(result.Applied);
        var itemResult = Assert.Single(result.ItemResults);
        Assert.False(itemResult.Applied);
        Assert.NotNull(itemResult.Error);
    }

    [Fact]
    public async Task OrderItemsArray_NoLineMatches_Ignore_NoErrorLogged()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-A");

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.Order, new
        {
            canonicalType = "order",
            rootMatch = new { sourcePath = "orderId", matchField = "Id" },
            itemsArray = new
            {
                arrayPath = "items",
                itemMatch = new { sourcePath = "sku", matchField = "Sku" },
                onNoMatch = "ignore",
                onMultipleMatches = "skip_and_log",
            },
            operation = new { name = "MarkDelivered", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });

        var body = Body($$"""{"orderId":"{{order.Id}}","items":[{"sku":"SKU-NONEXISTENT"}]}""");

        var result = await Evaluate(webhook, body, orderRepo: orderRepo.Object);

        var itemResult = Assert.Single(result.ItemResults);
        Assert.False(itemResult.Applied);
        Assert.Null(itemResult.Error);
    }

    [Fact]
    public async Task OrderItemsArray_ArrayPathDoesNotResolveToArray_Fails()
    {
        var tenantId = Guid.NewGuid();
        var order = MakeOrder(tenantId, "SKU-A");

        var orderRepo = NewOrderRepo();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var webhook = NewWebhook(CanonicalWebhookType.Order, new
        {
            canonicalType = "order",
            rootMatch = new { sourcePath = "orderId", matchField = "Id" },
            itemsArray = new
            {
                arrayPath = "items",
                itemMatch = new { sourcePath = "sku", matchField = "Sku" },
                onNoMatch = "skip_and_log",
                onMultipleMatches = "skip_and_log",
            },
            operation = new { name = "MarkDelivered", @params = new Dictionary<string, string>() },
            onNoMatch = "skip_and_log",
        });

        var body = Body($$"""{"orderId":"{{order.Id}}","items":"not-an-array"}""");

        var result = await Evaluate(webhook, body, orderRepo: orderRepo.Object);

        Assert.False(result.Applied);
        Assert.NotNull(result.Error);
    }

    // ── call_record ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CallRecord_SetCustomField_MatchedByContactIdExternal_CallsService()
    {
        var callRecord = CallRecord.CreateStub(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var definitionId = Guid.NewGuid();

        var callRecordRepo = NewCallRecordRepo();
        callRecordRepo.Setup(r => r.GetByContactIdExternalAsync("ext-123", It.IsAny<CancellationToken>())).ReturnsAsync(callRecord);

        var customFieldService = NewCustomFieldService();

        var webhook = NewWebhook(CanonicalWebhookType.CallRecord, new
        {
            canonicalType = "call_record",
            rootMatch = new { sourcePath = "contactId", matchField = "ContactIdExternal" },
            operation = new
            {
                name = "SetCustomField",
                @params = new Dictionary<string, string> { ["definitionId"] = definitionId.ToString(), ["valueSourcePath"] = "status" },
            },
            onNoMatch = "skip_and_log",
        });

        var body = Body("""{"contactId":"ext-123","status":"resolved"}""");

        var result = await Evaluate(webhook, body, callRecordRepo: callRecordRepo.Object, customFieldService: customFieldService.Object);

        Assert.True(result.Applied);
        customFieldService.Verify(s => s.SetValueAsync(callRecord.Id, definitionId, "resolved", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CallRecord_SetCustomField_MatchedById_CallsService()
    {
        var callRecord = CallRecord.CreateStub(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var definitionId = Guid.NewGuid();

        var callRecordRepo = NewCallRecordRepo();
        callRecordRepo.Setup(r => r.GetByIdAsync(callRecord.Id, It.IsAny<CancellationToken>())).ReturnsAsync(callRecord);

        var customFieldService = NewCustomFieldService();

        var webhook = NewWebhook(CanonicalWebhookType.CallRecord, new
        {
            canonicalType = "call_record",
            rootMatch = new { sourcePath = "callRecordId", matchField = "Id" },
            operation = new
            {
                name = "SetCustomField",
                @params = new Dictionary<string, string> { ["definitionId"] = definitionId.ToString(), ["valueSourcePath"] = "value" },
            },
            onNoMatch = "skip_and_log",
        });

        var body = Body($$"""{"callRecordId":"{{callRecord.Id}}","value":"42"}""");

        var result = await Evaluate(webhook, body, callRecordRepo: callRecordRepo.Object, customFieldService: customFieldService.Object);

        Assert.True(result.Applied);
        customFieldService.Verify(s => s.SetValueAsync(callRecord.Id, definitionId, "42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CallRecord_NotFound_SkipAndLog_Fails()
    {
        var callRecordRepo = NewCallRecordRepo();
        callRecordRepo.Setup(r => r.GetByContactIdExternalAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((CallRecord?)null);

        var webhook = NewWebhook(CanonicalWebhookType.CallRecord, new
        {
            canonicalType = "call_record",
            rootMatch = new { sourcePath = "contactId", matchField = "ContactIdExternal" },
            operation = new
            {
                name = "SetCustomField",
                @params = new Dictionary<string, string> { ["definitionId"] = Guid.NewGuid().ToString(), ["valueSourcePath"] = "status" },
            },
            onNoMatch = "skip_and_log",
        });

        var result = await Evaluate(webhook, Body("""{"contactId":"unknown","status":"x"}"""), callRecordRepo: callRecordRepo.Object);

        Assert.False(result.Applied);
        Assert.NotNull(result.Error);
    }

    // ── malformed config / payload ───────────────────────────────────────────

    [Fact]
    public async Task MalformedMappingConfigJson_Fails_DoesNotThrow()
    {
        var webhook = WebhookEndpoint.Create("Bad Config", CanonicalWebhookType.OrderLine);
        // Bypass the public factory's valid-default MappingConfig ("{}") by round-tripping
        // through SetMappingConfig with a deliberately invalid string.
        webhook.SetMappingConfig("{not valid json");

        var result = await Evaluate(webhook, Body("{}"));

        Assert.False(result.Applied);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task NoCanonicalTypeConfigured_Fails()
    {
        var webhook = WebhookEndpoint.Create("Unconfigured", CanonicalWebhookType.OrderLine);
        // Default MappingConfig is "{}" — no canonicalType set yet.

        var result = await Evaluate(webhook, Body("{}"));

        Assert.False(result.Applied);
        Assert.NotNull(result.Error);
    }
}
