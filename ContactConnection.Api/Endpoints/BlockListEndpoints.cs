using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class BlockListEndpoints
{
    public static IEndpointRouteBuilder MapBlockListEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/block-list");

        group.MapPost("",              Add).RequireAuthorization("BlocklistManage");
        group.MapGet("",               GetAll).RequireAuthorization("BlocklistView");
        group.MapPut("{id:guid}",      Update).RequireAuthorization("BlocklistManage");
        group.MapDelete("{id:guid}",   Remove).RequireAuthorization("BlocklistManage");

        return app;
    }

    // ── POST /api/v1/block-list ─────────────────────────────────────────────

    private static async Task<IResult> Add(
        AddBlockListEntryRequest req,
        IBlockListRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();

        var matchType = req.MatchType ?? BlockListMatchType.Exact;
        if (matchType != BlockListMatchType.Exact && matchType != BlockListMatchType.Prefix)
            return Results.BadRequest(new { error = "matchType must be 'exact' or 'prefix'." });

        var entry = BlockListEntry.Create(
            ctx.Current!.Id, req.PhoneNumber, matchType, req.Reason, req.ExpiresAt);

        await repo.AddAsync(entry, ct);
        await repo.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/block-list/{entry.Id}", ToResponse(entry));
    }

    // ── GET /api/v1/block-list ──────────────────────────────────────────────

    private static async Task<IResult> GetAll(
        IBlockListRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();

        var entries = await repo.GetAllAsync(ctx.Current!.Id, ct);
        return Results.Ok(entries.Select(ToResponse));
    }

    // ── PUT /api/v1/block-list/{id} ─────────────────────────────────────────

    private static async Task<IResult> Update(
        Guid id,
        UpdateBlockListEntryRequest req,
        IBlockListRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();

        var entry = await repo.GetByIdAsync(ctx.Current!.Id, id, ct);
        if (entry is null) return Results.NotFound();

        entry.Update(req.Reason, req.ExpiresAt);
        await repo.SaveChangesAsync(ct);

        return Results.Ok(ToResponse(entry));
    }

    // ── DELETE /api/v1/block-list/{id} ──────────────────────────────────────

    private static async Task<IResult> Remove(
        Guid id,
        IBlockListRepository repo,
        TenantContext ctx,
        CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();

        var entry = await repo.GetByIdAsync(ctx.Current!.Id, id, ct);
        if (entry is null) return Results.NotFound();

        entry.Remove();
        await repo.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    // ── Response mapping ─────────────────────────────────────────────────────

    private static object ToResponse(BlockListEntry e) => new
    {
        id          = e.Id,
        phoneNumber = e.PhoneNumber,
        matchType   = e.MatchType,
        reason      = e.Reason,
        expiresAt   = e.ExpiresAt,
        isActive    = e.IsActive,
        createdAt   = e.CreatedAt,
        updatedAt   = e.UpdatedAt
    };
}

public record AddBlockListEntryRequest(
    string PhoneNumber,
    string? MatchType,
    string? Reason,
    DateTimeOffset? ExpiresAt);

public record UpdateBlockListEntryRequest(
    string? Reason,
    DateTimeOffset? ExpiresAt);
