using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/v1/orders").RequireAuthorization();

        orders.MapGet("{id:guid}", GetById);
        orders.MapPost("{id:guid}/cancel", CancelOrder);
        orders.MapPost("{id:guid}/lines/{lineId:guid}/ship", ShipLine);
        orders.MapPost("{id:guid}/lines/{lineId:guid}/deliver", DeliverLine);
        orders.MapPost("{id:guid}/lines/{lineId:guid}/cancel", CancelLine);

        // Order creation lives under call-records (requires an active cart)
        var callRecords = app.MapGroup("/api/v1/call-records").RequireAuthorization();
        callRecords.MapGet("{callRecordId:guid}/order", GetByCallRecord);
        callRecords.MapPost("{callRecordId:guid}/order", CreateFromCart);

        return app;
    }

    // ── POST /api/v1/call-records/{callRecordId}/order ──────────────────────
    // Commits the current cart as an order. Confirms inventory reservations.
    // Returns 201 Created on first commit; 200 OK with existing order if already committed.

    private static async Task<IResult> CreateFromCart(
        Guid callRecordId,
        IOrderService orderService,
        ICommissionService commissions,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        try
        {
            var (order, created) = await orderService.CreateFromCartAsync(callRecordId, ct);
            if (created) await commissions.OrderSubmittedAsync(callRecordId, ct);
            return created
                ? Results.Created($"/api/v1/orders/{order.Id}", ToResponse(order))
                : Results.Ok(ToResponse(order));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { Message = ex.Message });
        }
    }

    // ── GET /api/v1/call-records/{callRecordId}/order ───────────────────────

    private static async Task<IResult> GetByCallRecord(
        Guid callRecordId,
        IOrderRepository orderRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var order = await orderRepo.GetByCallRecordIdAsync(callRecordId, ct);
        return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
    }

    // ── GET /api/v1/orders/{id} ─────────────────────────────────────────────

    private static async Task<IResult> GetById(
        Guid id,
        IOrderRepository orderRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var order = await orderRepo.GetByIdAsync(id, ct);
        return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
    }

    // ── POST /api/v1/orders/{id}/cancel ─────────────────────────────────────

    private static async Task<IResult> CancelOrder(
        Guid id,
        IOrderRepository orderRepo,
        ICommissionService commissions,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var order = await orderRepo.GetByIdAsync(id, ct);
        if (order is null) return Results.NotFound();

        if (order.Status is OrderStatus.Shipped or OrderStatus.Delivered)
            return Results.Conflict(new { Message = "Cannot cancel an order that has already shipped." });

        order.Cancel();
        await orderRepo.SaveChangesAsync(ct);
        if (order.CallRecordId is { } callId) await commissions.ReverseAsync(callId, CommissionTrigger.OrderCancelled, ct);
        return Results.Ok(ToResponse(order));
    }

    // ── POST /api/v1/orders/{id}/lines/{lineId}/ship ─────────────────────────

    private static async Task<IResult> ShipLine(
        Guid id,
        Guid lineId,
        ShipLineRequest request,
        IOrderRepository orderRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var order = await orderRepo.GetByIdAsync(id, ct);
        if (order is null) return Results.NotFound();

        var line = order.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null) return Results.NotFound();

        line.Ship(request.TrackingNumber);
        order.RefreshStatus();
        await orderRepo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(order));
    }

    // ── POST /api/v1/orders/{id}/lines/{lineId}/deliver ──────────────────────

    private static async Task<IResult> DeliverLine(
        Guid id,
        Guid lineId,
        IOrderRepository orderRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var order = await orderRepo.GetByIdAsync(id, ct);
        if (order is null) return Results.NotFound();

        var line = order.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null) return Results.NotFound();

        line.MarkDelivered();
        order.RefreshStatus();
        await orderRepo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(order));
    }

    // ── POST /api/v1/orders/{id}/lines/{lineId}/cancel ───────────────────────

    private static async Task<IResult> CancelLine(
        Guid id,
        Guid lineId,
        IOrderRepository orderRepo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var order = await orderRepo.GetByIdAsync(id, ct);
        if (order is null) return Results.NotFound();

        var line = order.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null) return Results.NotFound();

        if (line.FulfillmentStatus == OrderLineStatus.Shipped)
            return Results.Conflict(new { Message = "Cannot cancel a line that has already shipped." });

        line.Cancel();
        order.RefreshStatus();
        await orderRepo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(order));
    }

    // ── Response mapping ─────────────────────────────────────────────────────

    public static object ToResponse(Order o) => new
    {
        o.Id,
        o.TenantId,
        o.CallRecordId,
        o.Status,
        Financial = new
        {
            o.Subtotal,
            o.Shipping,
            o.SalesTax,
            o.Discount,
            o.Total
        },
        o.ShipMethod,
        o.ShippingZip,
        o.PaymentBreakdowns,
        Fulfillment = new
        {
            o.ShippedAt,
            o.DeliveredAt,
            o.CancelledAt
        },
        Lines = o.Lines.Select(ToLineResponse),
        o.CreatedAt,
        o.UpdatedAt
    };

    private static object ToLineResponse(OrderLine l) => new
    {
        l.Id,
        l.OfferId,
        l.ProductId,
        l.Sku,
        l.Description,
        l.Quantity,
        l.UnitPrice,
        l.ExtendedPrice,
        l.Shipping,
        l.SalesTax,
        l.Weight,
        l.ShippingExempt,
        l.TaxExempt,
        l.OnBackOrder,
        l.AutoShip,
        l.AutoShipIntervalDays,
        l.IsUpsell,
        l.MixMatchCode,
        l.ShipMethod,
        l.DeliveryMessage,
        l.Payments,
        l.PersonalizationAnswers,
        l.KitSelections,
        Fulfillment = new
        {
            l.FulfillmentStatus,
            l.TrackingNumber,
            l.ShippedAt,
            l.DeliveredAt,
            l.CancelledAt
        },
        l.CreatedAt,
        l.UpdatedAt
    };
}

public record ShipLineRequest(string TrackingNumber);
