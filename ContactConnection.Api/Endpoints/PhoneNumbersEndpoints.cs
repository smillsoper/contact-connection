using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class PhoneNumbersEndpoints
{
    public static IEndpointRouteBuilder MapPhoneNumbersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/phone-numbers").RequireAuthorization();

        group.MapPost("",                     Create);
        group.MapGet("",                      GetByCampaign);
        group.MapGet("{id:guid}",             GetById);
        group.MapPatch("{id:guid}",           Update);
        group.MapPost("{id:guid}/activate",   Activate);
        group.MapPost("{id:guid}/deactivate", Deactivate);

        return app;
    }

    // ── POST /api/v1/phone-numbers ───────────────────────────────────────────

    private static async Task<IResult> Create(
        CreatePhoneNumberRequest req,
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

    // ── Response shape ───────────────────────────────────────────────────────

    internal static object ToResponse(PhoneNumber pn) => new
    {
        pn.Id, pn.TenantId, pn.CampaignId, pn.Number, pn.Label, pn.IsActive,
        Campaign = pn.Campaign is null ? null : new { pn.Campaign.Id, pn.Campaign.Name },
        pn.CreatedAt, pn.UpdatedAt
    };
}

public record CreatePhoneNumberRequest(Guid CampaignId, string Number, string? Label = null);
public record UpdatePhoneNumberRequest(string? Label = null, Guid? CampaignId = null);
