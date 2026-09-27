using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Common;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Number providers — who houses a phone number: a carrier (Telnyx, Bandwidth) or a routing platform
/// (RingSquared) that delivers its calls to a delivery number of ours. See NumberProvider.
/// </summary>
public static class NumberProvidersEndpoints
{
    public static IEndpointRouteBuilder MapNumberProvidersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/number-providers").RequireAuthorization();

        group.MapGet("", GetAll);
        group.MapPost("", Create).RequireAuthorization("TenantAdmin");
        group.MapPut("{id:guid}", Update).RequireAuthorization("TenantAdmin");
        group.MapPost("{id:guid}/activate", Activate).RequireAuthorization("TenantAdmin");
        group.MapPost("{id:guid}/deactivate", Deactivate).RequireAuthorization("TenantAdmin");
        group.MapPost("{id:guid}/api-key", IssueApiKey).RequireAuthorization("TenantAdmin");
        group.MapDelete("{id:guid}/api-key", RevokeApiKey).RequireAuthorization("TenantAdmin");

        return app;
    }

    private static async Task<IResult> GetAll(INumberProviderRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        return Results.Ok((await repo.GetAllAsync(ct)).Select(ToResponse));
    }

    private static async Task<IResult> Create(
        SaveNumberProviderRequest req, INumberProviderRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        if (await repo.NameExistsAsync(req.Name ?? "", null, ct))
            return Results.Conflict(new { error = $"A provider named '{req.Name}' already exists." });
        NumberProvider provider;
        try
        {
            provider = NumberProvider.Create(ctx.Current!.Id, req.Name ?? "", req.Type ?? "", req.Notes);
            provider.Update(req.Name ?? "", req.Type ?? "", req.SipGatewayId, req.SourceIps, req.Notes);
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }

        await repo.AddAsync(provider, ct);
        await repo.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/number-providers/{provider.Id}", ToResponse(provider));
    }

    private static async Task<IResult> Update(
        Guid id, SaveNumberProviderRequest req, INumberProviderRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var provider = await repo.GetByIdAsync(id, ct);
        if (provider is null) return Results.NotFound();
        if (await repo.NameExistsAsync(req.Name ?? "", id, ct))
            return Results.Conflict(new { error = $"A provider named '{req.Name}' already exists." });
        try { provider.Update(req.Name ?? "", req.Type ?? "", req.SipGatewayId, req.SourceIps, req.Notes); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(provider));
    }

    private static Task<IResult> Activate(Guid id, INumberProviderRepository repo, TenantContext ctx, CancellationToken ct)
        => Mutate(id, repo, ctx, p => p.Activate(), ct);

    private static Task<IResult> Deactivate(Guid id, INumberProviderRepository repo, TenantContext ctx, CancellationToken ct)
        => Mutate(id, repo, ctx, p => p.Deactivate(), ct);

    private static Task<IResult> RevokeApiKey(Guid id, INumberProviderRepository repo, TenantContext ctx, CancellationToken ct)
        => Mutate(id, repo, ctx, p => p.RevokeApiKey(), ct);

    // ── POST /api/v1/number-providers/{id}/api-key ──────────────────────────
    // Issues (or rotates) the key a routing platform uses on our external routing endpoints. The
    // plaintext is returned ONCE here and never stored — only its hash.
    private static async Task<IResult> IssueApiKey(
        Guid id, INumberProviderRepository repo, TenantContext ctx, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var provider = await repo.GetByIdAsync(id, ct);
        if (provider is null) return Results.NotFound();
        var (plain, hash, prefix) = ApiKeyHasher.Issue();
        provider.SetApiKey(hash, prefix);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(new { ApiKey = plain, provider = ToResponse(provider) });
    }

    private static async Task<IResult> Mutate(
        Guid id, INumberProviderRepository repo, TenantContext ctx, Action<NumberProvider> change, CancellationToken ct)
    {
        if (!ctx.HasTenant) return Results.Unauthorized();
        var provider = await repo.GetByIdAsync(id, ct);
        if (provider is null) return Results.NotFound();
        change(provider);
        await repo.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(provider));
    }

    internal static object ToResponse(NumberProvider p) => new
    {
        p.Id, p.Name, p.Type, p.SipGatewayId, p.SourceIps, p.Notes, p.IsActive,
        HasApiKey = p.ApiKeyHash is not null, p.ApiKeyPrefix, p.ApiKeyIssuedAt,
        p.CreatedAt, p.UpdatedAt,
    };
}

public record SaveNumberProviderRequest(string? Name, string? Type, Guid? SipGatewayId = null, string? SourceIps = null, string? Notes = null);
