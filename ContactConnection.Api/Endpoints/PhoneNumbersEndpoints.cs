using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

public static class PhoneNumbersEndpoints
{
    public static IEndpointRouteBuilder MapPhoneNumbersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/phone-numbers").RequireAuthorization();

        // Changes need a tenant admin (S182 — they decide which account a number routes to); reading needs a sign-in.
        group.MapPost("",                        Create).RequireAuthorization("TenantAdmin");
        group.MapGet("",                         GetByCampaign);
        group.MapGet("{id:guid}",                GetById);
        group.MapPatch("{id:guid}",              Update).RequireAuthorization("TenantAdmin");
        group.MapPut("{id:guid}/flow",              SetFlow).RequireAuthorization("TenantAdmin");
        group.MapDelete("{id:guid}/flow",           RemoveFlow).RequireAuthorization("TenantAdmin");
        group.MapPut("{id:guid}/telephony-flow",    SetTelephonyFlow).RequireAuthorization("TenantAdmin");
        group.MapDelete("{id:guid}/telephony-flow", RemoveTelephonyFlow).RequireAuthorization("TenantAdmin");
        group.MapPost("{id:guid}/activate",         Activate).RequireAuthorization("TenantAdmin");
        group.MapPost("{id:guid}/deactivate",    Deactivate).RequireAuthorization("TenantAdmin");

        return app;
    }

    // ── POST /api/v1/phone-numbers ───────────────────────────────────────────

    private static async Task<IResult> Create(
        CreatePhoneNumberRequest req,
        INumberProviderRepository providers,
        IPhoneNumberRepository repo,
        ContactConnection.Infrastructure.Telephony.NumberOwnership ownership,
        ICampaignRepository campaigns,
        ContactConnection.Infrastructure.Data.ScopedTenantDbContextFactory dbf,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();

        // No campaign = straight into Reserve (S182).
        if (req.CampaignId is { } cid && await campaigns.GetByIdAsync(cid, ct) is null)
            return Results.NotFound(new { error = "Campaign not found." });

        var pn = PhoneNumber.Create(ctx.Current!.Id, req.CampaignId, req.Number, req.Label);
        await using (var db = dbf.Create())
        {
            var forms = PhoneNumber.Forms(pn.Number);
            if (await db.PhoneNumbers.AsNoTracking().AnyAsync(p => forms.Contains(p.Number), ct))
                return Results.Conflict(new { error = $"{pn.Number} is already in this account — move or reactivate it instead of adding it again." });
        }
        if (await ownership.ConflictAsync(pn.TenantId, pn.Number, pn.IsReleased, ct) is { } inUse)
            return Results.Conflict(new { error = inUse });
        if (await ApplyProviderAsync(pn, req.ProviderId, req.Role, req.ClientNumber, providers, ct) is { } providerError)
            return Results.BadRequest(new { error = providerError });
        await repo.AddAsync(pn, ct);
        await repo.SaveChangesAsync(ct);

        // Mirror to the global routing table so DNIS resolution works for IP-routing carriers
        await ownership.ApplyAsync(pn, ct);
        await ownership.SaveAsync(ct);

        return Results.Created($"/api/v1/phone-numbers/{pn.Id}", ToResponse(pn));
    }

    // ── GET /api/v1/phone-numbers?campaignId=x ──────────────────────────────

    private static async Task<IResult> GetByCampaign(
        Guid campaignId,
        IPhoneNumberRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var list = await repo.GetByCampaignIdAsync(campaignId, ct);
        return Results.Ok(list.Select(ToResponse));
    }

    // ── GET /api/v1/phone-numbers/{id} ──────────────────────────────────────

    private static async Task<IResult> GetById(
        Guid id,
        IPhoneNumberRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var pn = await repo.GetByIdAsync(id, ct);
        return pn is null ? Results.NotFound() : Results.Ok(ToResponse(pn));
    }

    // ── PATCH /api/v1/phone-numbers/{id} ────────────────────────────────────

    private static async Task<IResult> Update(
        Guid id,
        UpdatePhoneNumberRequest req,
        INumberProviderRepository providers,
        IPhoneNumberRepository repo,
        ContactConnection.Infrastructure.Telephony.NumberOwnership ownership,
        ICampaignRepository campaigns,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var pn = await repo.GetByIdAsync(id, ct);
        if (pn is null) return Results.NotFound();

        if (req.Label is not null)
            pn.UpdateLabel(req.Label);

        // Provider fields are updated together when any is sent (role defaults to the current one).
        if (req.ProviderId is not null || req.Role is not null || req.ClientNumber is not null || req.ClearProvider == true)
        {
            var providerId = req.ClearProvider == true ? null : req.ProviderId ?? pn.ProviderId;
            if (await ApplyProviderAsync(pn, providerId, req.Role ?? pn.Role, req.ClientNumber ?? pn.ClientNumber, providers, ct) is { } providerError)
                return Results.BadRequest(new { error = providerError });
        }

        if (req.CampaignId.HasValue)
        {
            if (await campaigns.GetByIdAsync(req.CampaignId.Value, ct) is null)
                return Results.NotFound(new { error = "Campaign not found." });
            if (await ownership.ConflictAsync(pn.TenantId, pn.Number, releasedHere: false, ct) is { } inUse)
                return Results.Conflict(new { error = inUse });
            pn.Reassign(req.CampaignId.Value);
        }
        else if (req.MoveToReserve == true)
        {
            pn.MoveToReserve();
        }

        await repo.SaveChangesAsync(ct);

        // Sync campaign reassignment to global routing table
        if (req.CampaignId.HasValue || req.MoveToReserve == true)
        {
            await ownership.ApplyAsync(pn, ct);
            await ownership.SaveAsync(ct);
        }

        return Results.Ok(ToResponse(pn));
    }

    // ── PUT /api/v1/phone-numbers/{id}/flow ─────────────────────────────────

    private static async Task<IResult> SetFlow(
        Guid id, SetPhoneNumberFlowRequest req,
        IPhoneNumberRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var pn = await repo.GetByIdAsync(id, ct);
        if (pn is null) return Results.NotFound();
        pn.AssignFlow(req.FlowId);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(pn));
    }

    // ── DELETE /api/v1/phone-numbers/{id}/flow ──────────────────────────────

    private static async Task<IResult> RemoveFlow(
        Guid id, IPhoneNumberRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var pn = await repo.GetByIdAsync(id, ct);
        if (pn is null) return Results.NotFound();
        pn.RemoveFlow();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(pn));
    }

    // ── PUT /api/v1/phone-numbers/{id}/telephony-flow ───────────────────────

    private static async Task<IResult> SetTelephonyFlow(
        Guid id, SetPhoneNumberFlowRequest req,
        IPhoneNumberRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var pn = await repo.GetByIdAsync(id, ct);
        if (pn is null) return Results.NotFound();
        pn.AssignTelephonyFlow(req.FlowId);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(pn));
    }

    // ── DELETE /api/v1/phone-numbers/{id}/telephony-flow ────────────────────

    private static async Task<IResult> RemoveTelephonyFlow(
        Guid id, IPhoneNumberRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var pn = await repo.GetByIdAsync(id, ct);
        if (pn is null) return Results.NotFound();
        pn.RemoveTelephonyFlow();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(pn));
    }

    // ── POST /api/v1/phone-numbers/{id}/activate ────────────────────────────

    private static async Task<IResult> Activate(
        Guid id,
        IPhoneNumberRepository repo,
        ContactConnection.Infrastructure.Telephony.NumberOwnership ownership,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var pn = await repo.GetByIdAsync(id, ct);
        if (pn is null) return Results.NotFound();
        // A number released from Reserve may meanwhile belong to another account (S182).
        if (await ownership.ConflictAsync(pn.TenantId, pn.Number, releasedHere: false, ct) is { } inUse)
            return Results.Conflict(new { error = inUse });
        pn.Activate();
        await repo.SaveChangesAsync(ct);

        await ownership.ApplyAsync(pn, ct);
        await ownership.SaveAsync(ct);

        return Results.Ok(ToResponse(pn));
    }

    // ── POST /api/v1/phone-numbers/{id}/deactivate ──────────────────────────

    private static async Task<IResult> Deactivate(
        Guid id,
        IPhoneNumberRepository repo,
        ContactConnection.Infrastructure.Telephony.NumberOwnership ownership,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var pn = await repo.GetByIdAsync(id, ct);
        if (pn is null) return Results.NotFound();
        pn.Deactivate();
        await repo.SaveChangesAsync(ct);

        await ownership.ApplyAsync(pn, ct);
        await ownership.SaveAsync(ct);

        return Results.Ok(ToResponse(pn));
    }

    /// <summary>Validates and applies provider / role / client number. Null when OK, else the error.</summary>
    internal static async Task<string?> ApplyProviderAsync(
        PhoneNumber pn, Guid? providerId, string? role, string? clientNumber,
        INumberProviderRepository providers, CancellationToken ct)
    {
        role ??= PhoneNumberRole.Hosted;
        if (providerId is { } id)
        {
            var provider = await providers.GetByIdAsync(id, ct);
            if (provider is null) return "Number provider not found.";
            if (role == PhoneNumberRole.RoutingDelivery && provider.Type != NumberProviderType.RoutingPlatform)
                return $"'{provider.Name}' is a carrier — only a routing platform delivers to a routing delivery number.";
        }
        else if (role == PhoneNumberRole.RoutingDelivery)
        {
            return "A routing delivery number needs its routing platform provider.";
        }
        try { pn.SetProvider(providerId, role, clientNumber); }
        catch (ArgumentException ex) { return ex.Message; }
        return null;
    }

    // ── Response shape ───────────────────────────────────────────────────────

    internal static object ToResponse(PhoneNumber pn) => new
    {
        pn.Id, pn.TenantId, pn.CampaignId, pn.Number, pn.Label, pn.IsActive, pn.FlowId, pn.TelephonyFlowId,
        pn.ReservedAt, pn.InReserve, pn.IsReleased,
        pn.ProviderId, pn.Role, pn.ClientNumber,
        Campaign = pn.Campaign is null ? null : new { pn.Campaign.Id, pn.Campaign.Name },
        pn.CreatedAt, pn.UpdatedAt
    };
}

public record CreatePhoneNumberRequest(Guid? CampaignId, string Number, string? Label = null,
    Guid? ProviderId = null, string? Role = null, string? ClientNumber = null);
public record UpdatePhoneNumberRequest(string? Label = null, Guid? CampaignId = null,
    Guid? ProviderId = null, string? Role = null, string? ClientNumber = null, bool? ClearProvider = null, bool? MoveToReserve = null);
public record SetPhoneNumberFlowRequest(Guid FlowId);
