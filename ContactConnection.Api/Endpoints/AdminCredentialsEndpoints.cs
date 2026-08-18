using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace ContactConnection.Api.Endpoints;

public static class AdminCredentialsEndpoints
{
    public static IEndpointRouteBuilder MapAdminCredentialsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/credentials")
            .RequireAuthorization("TenantAdmin");

        group.MapGet("", ListAll);
        group.MapPut("{keyName}", Upsert);
        group.MapDelete("{keyName}", Delete);
        group.MapGet("{keyName}/audit", ListAudit);

        return app;
    }

    private static async Task<IResult> ListAll(
        ITenantCredentialStore store,
        CancellationToken ct)
    {
        var items = await store.ListAsync(ct);
        return Results.Ok(items.Select(i => new { i.KeyName, i.UpdatedOn }));
    }

    private static async Task<IResult> Upsert(
        string keyName,
        UpsertCredentialRequest request,
        ITenantCredentialStore store,
        [FromKeyedServices("tenant")] ICredentialAuditService audit,
        HttpContext http,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
            return Results.BadRequest(new { error = "Value is required." });

        var actor = ActorResolver.Resolve(http.User);
        if (actor is null) return Results.Unauthorized();

        await store.SetAsync(keyName, request.Value, ct);
        await audit.RecordAsync(keyName, CredentialAuditAction.Set, actor.Value.Id, actor.Value.Name, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(
        string keyName,
        ITenantCredentialStore store,
        [FromKeyedServices("tenant")] ICredentialAuditService audit,
        HttpContext http,
        CancellationToken ct)
    {
        var actor = ActorResolver.Resolve(http.User);
        if (actor is null) return Results.Unauthorized();

        await store.DeleteAsync(keyName, ct);
        await audit.RecordAsync(keyName, CredentialAuditAction.Delete, actor.Value.Id, actor.Value.Name, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListAudit(
        string keyName,
        [FromKeyedServices("tenant")] ICredentialAuditService audit,
        CancellationToken ct)
    {
        return Results.Ok(await audit.ListAsync(keyName, ct));
    }
}
