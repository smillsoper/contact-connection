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
        group.MapGet("{id:guid}", GetById);
        group.MapGet("{id:guid}/cart", GetCart);
        group.MapPut("{id:guid}/cart", SetCart);

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
    // Accepts a CartDocument, re-calculates totals, then swaps inventory reservations.
    // Returns 409 Conflict with a list of SKUs if any item cannot be reserved.

    private static async Task<IResult> SetCart(
        Guid id,
        CartDocument cartRequest,
        ICallRecordRepository callRecords,
        IPricingService pricing,
        IInventoryService inventory,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var record = await callRecords.GetByIdWithInteractionsAsync(id, ct);
        if (record is null) return Results.NotFound();

        // Release reservations held by the existing cart (if any) before applying the new one.
        await inventory.ReleaseCartAsync(record.Cart, ct);

        // Attempt to reserve inventory for the incoming cart.
        var unavailable = await inventory.ReserveCartAsync(cartRequest, ct);
        if (unavailable.Count > 0)
        {
            // Restore the old cart's reservations so the call record is consistent.
            if (record.Cart is not null)
                await inventory.ReserveCartAsync(record.Cart, ct);

            return Results.Conflict(new { Message = "Insufficient inventory.", UnavailableSkus = unavailable });
        }

        var calculated = await pricing.CalculateTotalsAsync(cartRequest, ct);
        record.SetCart(calculated);
        await callRecords.SaveChangesAsync(ct);

        return Results.Ok(calculated);
    }

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
