using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class PhoneNumbersEndpoints
{
    public static IEndpointRouteBuilder MapPhoneNumbersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/phone-numbers").RequireAuthorization();

        group.MapPost("",                        Create);
        group.MapGet("",                         GetByCampaign);
        group.MapGet("{id:guid}",                GetById);
        group.MapPatch("{id:guid}",              Update);
        group.MapPut("{id:guid}/flow",              SetFlow);
        group.MapDelete("{id:guid}/flow",           RemoveFlow);
        group.MapPut("{id:guid}/telephony-flow",    SetTelephonyFlow);
        group.MapDelete("{id:guid}/telephony-flow", RemoveTelephonyFlow);
        group.MapPost("{id:guid}/activate",         Activate);
        group.MapPost("{id:guid}/deactivate",    Deactivate);

        return app;
    }

    // ── POST /api/v1/phone-numbers ───────────────────────────────────────────

    private static async Task<IResult> Create(
        CreatePhoneNumberRequest req,
        INumberProviderRepository providers,
        IPhoneNumberRepository repo,
        IPhoneNumberRoutingRepository routing,
        ICampaignRepository campaigns,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();

        if (await campaigns.GetByIdAsync(req.CampaignId, ct) is null)
            return Results.NotFound(new { error = "Campaign not found." });

        var pn = PhoneNumber.Create(ctx.Current!.Id, req.CampaignId, req.Number, req.Label);
        if (await ApplyProviderAsync(pn, req.ProviderId, req.Role, req.ClientNumber, providers, ct) is { } providerError)
            return Results.BadRequest(new { error = providerError });
        await repo.AddAsync(pn, ct);
        await repo.SaveChangesAsync(ct);

        // Mirror to global routing table so DNIS resolution works for IP-routing carriers
        await routing.UpsertAsync(pn.Number, ctx.Current.Id, pn.CampaignId, isActive: true, ct);
        await routing.SaveChangesAsync(ct);

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
        IPhoneNumberRoutingRepository routing,
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
            pn.Reassign(req.CampaignId.Value);
        }

        await repo.SaveChangesAsync(ct);

        // Sync campaign reassignment to global routing table
        if (req.CampaignId.HasValue)
        {
            await routing.UpsertAsync(pn.Number, ctx.Current!.Id, pn.CampaignId, pn.IsActive, ct);
            await routing.SaveChangesAsync(ct);
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
        IPhoneNumberRoutingRepository routing,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var pn = await repo.GetByIdAsync(id, ct);
        if (pn is null) return Results.NotFound();
        pn.Activate();
        await repo.SaveChangesAsync(ct);

        await routing.SetActiveAsync(pn.Number, isActive: true, ct);
        await routing.SaveChangesAsync(ct);

        return Results.Ok(ToResponse(pn));
    }

    // ── POST /api/v1/phone-numbers/{id}/deactivate ──────────────────────────

    private static async Task<IResult> Deactivate(
        Guid id,
        IPhoneNumberRepository repo,
        IPhoneNumberRoutingRepository routing,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var pn = await repo.GetByIdAsync(id, ct);
        if (pn is null) return Results.NotFound();
        pn.Deactivate();
        await repo.SaveChangesAsync(ct);

        await routing.SetActiveAsync(pn.Number, isActive: false, ct);
        await routing.SaveChangesAsync(ct);

        return Results.Ok(ToResponse(pn));
    }

    /// <summary>Validates and applies provider / role / client number. Null when OK, else the error.</summary>
    private static async Task<string?> ApplyProviderAsync(
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
        pn.ProviderId, pn.Role, pn.ClientNumber,
        Campaign = pn.Campaign is null ? null : new { pn.Campaign.Id, pn.Campaign.Name },
        pn.CreatedAt, pn.UpdatedAt
    };
}

public record CreatePhoneNumberRequest(Guid CampaignId, string Number, string? Label = null,
    Guid? ProviderId = null, string? Role = null, string? ClientNumber = null);
public record UpdatePhoneNumberRequest(string? Label = null, Guid? CampaignId = null,
    Guid? ProviderId = null, string? Role = null, string? ClientNumber = null, bool? ClearProvider = null);
public record SetPhoneNumberFlowRequest(Guid FlowId);
