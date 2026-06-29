using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class CampaignExternalNumbersEndpoints
{
    public static IEndpointRouteBuilder MapCampaignExternalNumbersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/campaigns/{id:guid}").RequireAuthorization();

        group.MapGet("external-numbers",                          GetAll);
        group.MapPost("external-numbers",                         Add);
        group.MapDelete("external-numbers/{numberId:guid}",       Remove);
        group.MapGet("client-transfer-numbers",                   GetClientTransferNumbers);

        return app;
    }

    // ── GET /api/v1/campaigns/{id}/external-numbers ──────────────────────────

    private static async Task<IResult> GetAll(
        Guid id, ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        if (await repo.GetByIdAsync(id, ct) is null) return Results.NotFound();
        var numbers = await repo.GetExternalNumbersAsync(id, ct);
        return Results.Ok(numbers.Select(ToResponse));
    }

    // ── POST /api/v1/campaigns/{id}/external-numbers ─────────────────────────

    private static async Task<IResult> Add(
        Guid id, AddExternalNumberRequest req,
        ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var campaign = await repo.GetByIdAsync(id, ct);
        if (campaign is null) return Results.NotFound();

        var existing = await repo.GetExternalNumbersAsync(id, ct);
        var number = CampaignExternalNumber.Create(
            id, ctx.Current!.Id, req.Label, req.Number, existing.Count);

        await repo.AddExternalNumberAsync(number, ct);
        await repo.SaveChangesAsync(ct);
        return Results.Created("", ToResponse(number));
    }

    // ── DELETE /api/v1/campaigns/{id}/external-numbers/{numberId} ────────────

    private static async Task<IResult> Remove(
        Guid id, Guid numberId,
        ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var number = await repo.GetExternalNumberByIdAsync(id, numberId, ct);
        if (number is null) return Results.NotFound();
        number.Deactivate();
        await repo.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ── GET /api/v1/campaigns/{id}/client-transfer-numbers ───────────────────
    // Used by the agent softphone: returns all active external numbers from
    // all manual-outbound campaigns belonging to the same client as {id}.

    private static async Task<IResult> GetClientTransferNumbers(
        Guid id, ICampaignRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var numbers = await repo.GetClientTransferNumbersAsync(id, ct);
        return Results.Ok(numbers.Select(n => new
        {
            n.Id, n.CampaignId,
            CampaignName = n.Campaign?.Name,
            CampaignFlowId = n.Campaign?.FlowId,
            n.Label, n.Number,
        }));
    }

    private static object ToResponse(CampaignExternalNumber n) => new
    { n.Id, n.CampaignId, n.Label, n.Number, n.DisplayOrder, n.IsActive, n.CreatedAt };
}

public record AddExternalNumberRequest(string Label, string Number);
