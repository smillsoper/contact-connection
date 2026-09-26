using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Commerce;
using Microsoft.AspNetCore.Mvc;

namespace ContactConnection.Api.Endpoints;

public static class CallRecordsEndpoints
{
    public static IEndpointRouteBuilder MapCallRecordsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/call-records").RequireAuthorization();

        group.MapPost("inbound", CreateInbound);
        group.MapPost("outbound", CreateOutbound);
        group.MapPost("manual", CreateManual);
        group.MapGet("{id:guid}", GetById);
        group.MapGet("{id:guid}/cart", GetCart);
        group.MapPut("{id:guid}/cart", SetCart);
        group.MapPost("{id:guid}/cart/items", AddCartItem);
        group.MapPatch("{id:guid}/cart/items/{itemIndex:int}", UpdateCartItemQuantity);
        group.MapDelete("{id:guid}/cart/items/{itemIndex:int}", RemoveCartItem);
        group.MapPost("{id:guid}/payments/authorize", AuthorizePayment);
        group.MapPost("{id:guid}/payments/void", VoidPayment);

        return app;
    }

    // ── POST /api/v1/call-records/inbound ───────────────────────────────────
    // Called by the agent UI when answering an incoming call via JsSIP.
    // Creates a stub CallRecord with caller info; agent resolves client/campaign during the call.

    private static async Task<IResult> CreateInbound(
        InboundCallRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        ICallRecordRepository callRecords,
        IPhoneNumberRepository phoneNumbers,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();

        var agentIdClaim = user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(agentIdClaim, out var agentId)) return Results.Unauthorized();

        var record = CallRecord.CreateInbound(
            tenantId: tenantContext.Current.Id,
            callerId: request.CallerNumber,
            agentId: agentId,
            contactIdExternal: request.ChannelUuid);

        await callRecords.AddAsync(record, ct);
        await callRecords.SaveChangesAsync(ct);

        // Resolve campaign from the DID if provided — used by softphone for transfer number context
        Guid? campaignId = null;
        if (!string.IsNullOrWhiteSpace(request.CalledNumber))
        {
            var phoneNumber = await phoneNumbers.GetByNumberAsync(request.CalledNumber, ct);
            campaignId = phoneNumber?.CampaignId;
        }

        return Results.Created($"/api/v1/call-records/{record.Id}", new { id = record.Id, campaignId });
    }

    // ── POST /api/v1/call-records/outbound ─────────────────────────────────
    // Called by the agent UI when an outbound JsSIP call is accepted by the remote party.

    private static async Task<IResult> CreateOutbound(
        OutboundCallRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        ICallRecordRepository callRecords,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();

        var agentIdClaim = user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(agentIdClaim, out var agentId)) return Results.Unauthorized();

        var record = CallRecord.CreateOutbound(
            tenantId: tenantContext.Current.Id,
            dialedNumber: request.DialedNumber,
            agentId: agentId);

        await callRecords.AddAsync(record, ct);
        await callRecords.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/call-records/{record.Id}", new { id = record.Id });
    }

    // ── POST /api/v1/call-records/manual ────────────────────────────────────
    // Backs the CRM Flow Designer's "Select flow → Start" manual test toolbar — no real call,
    // just a stub record so call-record-scoped features (cart, custom fields, etc.) have
    // something to attach to while previewing a flow.

    private static async Task<IResult> CreateManual(
        System.Security.Claims.ClaimsPrincipal user,
        ICallRecordRepository callRecords,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();

        var agentIdClaim = user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(agentIdClaim, out var agentId)) return Results.Unauthorized();

        var record = CallRecord.CreateManual(tenantContext.Current.Id, agentId);

        await callRecords.AddAsync(record, ct);
        await callRecords.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/call-records/{record.Id}", new { id = record.Id });
    }

    private static async Task<IResult> GetById(
        Guid id,
        ICallRecordRepository callRecords,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var record = await callRecords.GetByIdWithInteractionsAsync(id, ct);

        return record is null ? Results.NotFound() : Results.Ok(ToResponse(record));
    }

    // ── GET /api/v1/call-records/{id}/cart ──────────────────────────────────

    private static async Task<IResult> GetCart(
        Guid id,
        ICallRecordRepository callRecords,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var record = await callRecords.GetByIdWithInteractionsAsync(id, ct);
        if (record is null) return Results.NotFound();

        return record.Cart is null ? Results.NoContent() : Results.Ok(record.Cart);
    }

    // ── PUT /api/v1/call-records/{id}/cart ──────────────────────────────────
    // Replaces the whole cart document. Returns 409 Conflict with a list of SKUs if any item
    // cannot be reserved.

    private static async Task<IResult> SetCart(
        Guid id,
        CartDocument cartRequest,
        ICartService cart,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        try
        {
            var result = await cart.ReplaceCartAsync(id, cartRequest, ct);
            return CartResult(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    // ── POST /api/v1/call-records/{id}/cart/items ───────────────────────────
    // Adds one line item for the given offer/quantity. Returns 409 Conflict with a list of SKUs
    // if the item cannot be reserved (e.g. OutOfStock/Discontinued or insufficient NoBackorder stock).

    private static async Task<IResult> AddCartItem(
        Guid id,
        AddCartItemRequest request,
        ICartService cart,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        try
        {
            var result = await cart.AddItemAsync(id, request.OfferId, request.Quantity, ct);
            return CartResult(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    // ── PATCH /api/v1/call-records/{id}/cart/items/{itemIndex} ──────────────
    // Updates a line item's quantity, re-resolving its payment schedule (QPB/MixMatch thresholds
    // can shift). Returns 409 Conflict with a list of SKUs on an inventory conflict.

    private static async Task<IResult> UpdateCartItemQuantity(
        Guid id,
        int itemIndex,
        UpdateCartItemQuantityRequest request,
        ICartService cart,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        try
        {
            var result = await cart.UpdateQuantityAsync(id, itemIndex, request.Quantity, ct);
            return CartResult(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    // ── DELETE /api/v1/call-records/{id}/cart/items/{itemIndex} ─────────────

    private static async Task<IResult> RemoveCartItem(
        Guid id,
        int itemIndex,
        ICartService cart,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        try
        {
            var result = await cart.RemoveItemAsync(id, itemIndex, ct);
            return CartResult(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    // ── POST /api/v1/call-records/{id}/payments/authorize ───────────────────
    // Thin passthrough to IPaymentService — lets an authorize_payment flow node's outcome be tested
    // directly against a real gateway sandbox, and doubles as a future manual/admin "charge this
    // card" action. Field-key names default to tf_secure_collect's own conventional keys.

    private static async Task<IResult> AuthorizePayment(
        Guid id,
        AuthorizePaymentRequest request,
        IPaymentService payments,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        try
        {
            var result = await payments.AuthorizeAsync(
                id, request.Provider ?? "authorize_net",
                request.CardNumberField ?? "card_number",
                request.ExpField ?? "exp",
                request.CvvField ?? "cvv",
                request.ZipField, request.ZipOverride,
                request.FixedAmount, ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    // ── POST /api/v1/call-records/{id}/payments/void ─────────────────────────

    private static async Task<IResult> VoidPayment(
        Guid id,
        IPaymentService payments,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var result = await payments.VoidMostRecentAsync(id, ct);
        return Results.Ok(result);
    }

    // The shared frontend fetch wrapper (api/client.ts) only surfaces a `{error}` field from a
    // non-2xx body as its thrown Error's message — compose the SKU list directly into that string
    // so a 409 shows something a caller can render as-is, not raw JSON.
    private static IResult CartResult(CartOperationResult result) =>
        result.Succeeded
            ? Results.Ok(result.Cart)
            : Results.Conflict(new
            {
                error = $"Insufficient inventory for: {string.Join(", ", result.UnavailableSkus)}.",
                unavailableSkus = result.UnavailableSkus,
            });

    private static object ToResponse(CallRecord r) => new
    {
        r.Id,
        r.TenantId,
        r.ClientId,
        r.CampaignId,
        r.AgentId,
        r.Source,
        r.RecordType,
        r.OverallStatus,
        CallerIdentity = new
        {
            r.CallerId,
            r.AccountNumber,
            r.FirstName,
            r.LastName,
            r.Email,
            r.Phone
        },
        Timing = new
        {
            r.CallStartAt,
            r.CallEndAt,
            r.HandleTimeSeconds
        },
        Financial = new
        {
            r.TotalAmount,
            r.TaxAmount,
            r.PaymentStatus
        },
        Fulfillment = new
        {
            r.FulfillmentStatus,
            r.TrackingNumber
        },
        r.Addresses,
        r.CommitmentEvents,
        r.Cart,
        r.RecordingUrl,
        Interactions = r.Interactions.Select(i => new
        {
            i.Id,
            i.InteractionNumber,
            i.Type,
            i.FlowId,
            i.FlowVersion,
            i.Disposition,
            i.Status,
            i.CommitmentEvents,
            i.CartId,
            i.StartedAt,
            i.CompletedAt
        }),
        r.CreatedAt,
        r.UpdatedAt,
        TelephonyTrace = r.TelephonyEvents,
    };
}

public record InboundCallRequest(string? CallerNumber, string? CallerName, string? ChannelUuid, string? CalledNumber);
public record OutboundCallRequest(string? DialedNumber);
public record AddCartItemRequest(Guid OfferId, int Quantity);
public record UpdateCartItemQuantityRequest(int Quantity);
public record AuthorizePaymentRequest(
    string? Provider, string? CardNumberField, string? ExpField, string? CvvField, string? ZipField,
    string? ZipOverride, decimal? FixedAmount);
