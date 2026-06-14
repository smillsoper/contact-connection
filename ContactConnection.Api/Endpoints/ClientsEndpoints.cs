using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class ClientsEndpoints
{
    public static IEndpointRouteBuilder MapClientsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/clients").RequireAuthorization();

        group.MapPost("",                     Create);
        group.MapGet("",                      GetAll);
        group.MapGet("{id:guid}",             GetById);
        group.MapPut("{id:guid}",             Update);
        group.MapPost("{id:guid}/activate",   Activate);
        group.MapPost("{id:guid}/deactivate", Deactivate);

        return app;
    }

    // ── POST /api/v1/clients ─────────────────────────────────────────────────

    private static async Task<IResult> Create(
        CreateClientRequest req,
        IClientRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var client = Client.Create(tenantContext.Current!.Id, req.Name, req.AccountNumber);
        await repo.AddAsync(client, ct);
        await repo.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/clients/{client.Id}", ToResponse(client));
    }

    // ── GET /api/v1/clients ──────────────────────────────────────────────────

    private static async Task<IResult> GetAll(
        IClientRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var list = await repo.GetAllAsync(ct);
        return Results.Ok(list.Select(ToResponse));
    }

    // ── GET /api/v1/clients/{id} ─────────────────────────────────────────────

    private static async Task<IResult> GetById(
        Guid id,
        IClientRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var client = await repo.GetByIdAsync(id, ct);
        return client is null ? Results.NotFound() : Results.Ok(ToResponse(client));
    }

    // ── PUT /api/v1/clients/{id} ─────────────────────────────────────────────

    private static async Task<IResult> Update(
        Guid id,
        UpdateClientRequest req,
        IClientRepository repo,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var client = await repo.GetByIdAsync(id, ct);
        if (client is null) return Results.NotFound();

        client.Update(req.Name, req.AccountNumber);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(client));
    }

    // ── POST /api/v1/clients/{id}/activate ──────────────────────────────────

    private static async Task<IResult> Activate(
        Guid id, IClientRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var client = await repo.GetByIdAsync(id, ct);
        if (client is null) return Results.NotFound();
        client.Activate();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(client));
    }

    // ── POST /api/v1/clients/{id}/deactivate ────────────────────────────────

    private static async Task<IResult> Deactivate(
        Guid id, IClientRepository repo, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var client = await repo.GetByIdAsync(id, ct);
        if (client is null) return Results.NotFound();
        client.Deactivate();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(client));
    }

    // ── Response shape ───────────────────────────────────────────────────────

    internal static object ToResponse(Client c) => new
    {
        c.Id, c.TenantId, c.Name, c.AccountNumber, c.Status,
        Campaigns = c.Campaigns.Select(cam => new { cam.Id, cam.Name, cam.Slug, cam.Status }),
        c.CreatedAt, c.UpdatedAt
    };
}

public record CreateClientRequest(string Name, string? AccountNumber = null);
public record UpdateClientRequest(string Name, string? AccountNumber = null);
