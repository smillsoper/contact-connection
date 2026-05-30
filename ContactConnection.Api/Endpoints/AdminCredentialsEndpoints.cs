using ContactConnection.Application.Interfaces.Services;

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
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
            return Results.BadRequest(new { error = "Value is required." });

        await store.SetAsync(keyName, request.Value, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(
        string keyName,
        ITenantCredentialStore store,
        CancellationToken ct)
    {
        await store.DeleteAsync(keyName, ct);
        return Results.NoContent();
    }
}
