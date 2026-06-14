using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class SipGatewaysEndpoints
{
    public static IEndpointRouteBuilder MapSipGatewaysEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/sip-gateways").RequireAuthorization();

        group.MapPost("",                     Create);
        group.MapGet("",                      GetByTenant);
        group.MapGet("{id:guid}",             GetById);
        group.MapPut("{id:guid}",             Update);
        group.MapPost("{id:guid}/activate",   Activate);
        group.MapPost("{id:guid}/deactivate", Deactivate);

        return app;
    }

    // ── POST /api/v1/sip-gateways ───────────────────────────────────────────

    private static async Task<IResult> Create(
        CreateSipGatewayRequest req,
        ISipGatewayRepository repo,
        ITenantRepository tenants,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(req.TenantId, ct);
        if (tenant is null) return Results.NotFound(new { error = "Tenant not found." });

        var gateway = SipGateway.Create(
            req.TenantId, req.Name, req.Proxy, req.Username, req.Password,
            req.Register, req.Transport ?? SipTransport.Udp, req.FromDomain);

        await repo.AddAsync(gateway, ct);
        await repo.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/sip-gateways/{gateway.Id}", ToResponse(gateway));
    }

    // ── GET /api/v1/sip-gateways?tenantId=x ────────────────────────────────

    private static async Task<IResult> GetByTenant(
        Guid tenantId,
        ISipGatewayRepository repo,
        CancellationToken ct)
    {
        var list = await repo.GetByTenantIdAsync(tenantId, ct);
        return Results.Ok(list.Select(ToResponse));
    }

    // ── GET /api/v1/sip-gateways/{id} ───────────────────────────────────────

    private static async Task<IResult> GetById(
        Guid id,
        ISipGatewayRepository repo,
        CancellationToken ct)
    {
        var gateway = await repo.GetByIdAsync(id, ct);
        return gateway is null ? Results.NotFound() : Results.Ok(ToResponse(gateway));
    }

    // ── PUT /api/v1/sip-gateways/{id} ───────────────────────────────────────

    private static async Task<IResult> Update(
        Guid id,
        UpdateSipGatewayRequest req,
        ISipGatewayRepository repo,
        CancellationToken ct)
    {
        var gateway = await repo.GetByIdAsync(id, ct);
        if (gateway is null) return Results.NotFound();

        gateway.Update(req.Proxy, req.Username, req.Password,
            req.Register, req.Transport ?? SipTransport.Udp, req.FromDomain, req.CodecPrefs);
        await repo.SaveChangesAsync(ct);

        return Results.Ok(ToResponse(gateway));
    }

    // ── POST /api/v1/sip-gateways/{id}/activate ─────────────────────────────

    private static async Task<IResult> Activate(Guid id, ISipGatewayRepository repo, CancellationToken ct)
    {
        var gateway = await repo.GetByIdAsync(id, ct);
        if (gateway is null) return Results.NotFound();
        gateway.Activate();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(gateway));
    }

    // ── POST /api/v1/sip-gateways/{id}/deactivate ───────────────────────────

    private static async Task<IResult> Deactivate(Guid id, ISipGatewayRepository repo, CancellationToken ct)
    {
        var gateway = await repo.GetByIdAsync(id, ct);
        if (gateway is null) return Results.NotFound();
        gateway.Deactivate();
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(gateway));
    }

    // ── Response shape ───────────────────────────────────────────────────────

    internal static object ToResponse(SipGateway g) => new
    {
        g.Id, g.TenantId, g.Name, g.Proxy, g.FromDomain, g.Username,
        g.Register, g.Transport, g.CodecPrefs, g.IsActive, g.CreatedAt, g.UpdatedAt
        // Password intentionally omitted from responses
    };
}

public record CreateSipGatewayRequest(
    Guid TenantId, string Name, string Proxy, string Username, string Password,
    bool Register = true, string? Transport = null, string? FromDomain = null);

public record UpdateSipGatewayRequest(
    string Proxy, string Username, string Password, bool Register,
    string? Transport = null, string? FromDomain = null, string? CodecPrefs = null);
