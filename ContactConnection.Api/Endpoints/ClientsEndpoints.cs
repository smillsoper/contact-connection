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

        group.MapGet("{id:guid}/order-number-sequence",    GetOrderNumberSequence);
        group.MapPut("{id:guid}/order-number-sequence",    PutOrderNumberSequence).RequireAuthorization("TenantAdmin");
        group.MapDelete("{id:guid}/order-number-sequence", DeleteOrderNumberSequence).RequireAuthorization("TenantAdmin");

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

    // ── GET /api/v1/clients/{id}/order-number-sequence ───────────────────────

    private static async Task<IResult> GetOrderNumberSequence(
        Guid id, IOrderNumberSequenceRepository sequences, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        // 200 either way — "no sequence" is a normal, expected state for most clients (order numbers
        // are opt-in), not a missing resource.
        var sequence = await sequences.GetByClientIdAsync(id, ct);
        return sequence is null
            ? Results.Ok(new { Configured = false, ClientId = id })
            : Results.Ok(ToSequenceResponse(sequence));
    }

    // ── PUT /api/v1/clients/{id}/order-number-sequence ───────────────────────
    // Create-or-update: a client has at most one sequence.

    private static async Task<IResult> PutOrderNumberSequence(
        Guid id,
        PutOrderNumberSequenceRequest req,
        IClientRepository clients,
        IOrderNumberSequenceRepository sequences,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        if (await clients.GetByIdAsync(id, ct) is null) return Results.NotFound();

        var prefix = req.Prefix?.Trim() ?? "";
        var suffix = req.Suffix?.Trim() ?? "";
        var sequence = await sequences.GetByClientIdAsync(id, ct);
        try
        {
            if (sequence is null)
            {
                sequence = OrderNumberSequence.Create(tenantContext.Current!.Id, id, prefix, suffix, req.Width, req.NextValue);
                await sequences.AddAsync(sequence, ct);
            }
            else
            {
                sequence.Update(prefix, suffix, req.Width, req.NextValue);
            }
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        await sequences.SaveChangesAsync(ct);
        return Results.Ok(ToSequenceResponse(sequence));
    }

    // ── DELETE /api/v1/clients/{id}/order-number-sequence ────────────────────

    private static async Task<IResult> DeleteOrderNumberSequence(
        Guid id, IOrderNumberSequenceRepository sequences, TenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var sequence = await sequences.GetByClientIdAsync(id, ct);
        if (sequence is null) return Results.NotFound();
        await sequences.DeleteAsync(sequence, ct);
        await sequences.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static object ToSequenceResponse(OrderNumberSequence s) => new
    {
        Configured = true,
        s.Id, s.ClientId, s.Prefix, s.Suffix, s.Width, s.NextValue,
        NextOrderNumber = s.Format(s.NextValue),
        s.CreatedAt, s.UpdatedAt
    };

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
public record PutOrderNumberSequenceRequest(string? Prefix, string? Suffix, int Width, long NextValue);
