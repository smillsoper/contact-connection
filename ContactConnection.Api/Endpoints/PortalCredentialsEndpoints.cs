using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Endpoints;

public static class PortalCredentialsEndpoints
{
    public static IEndpointRouteBuilder MapPortalCredentialsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/portal/credentials")
            .RequireAuthorization("PlatformAdmin");

        group.MapGet("", ListAll);
        group.MapPut("{keyName}", Upsert);
        group.MapDelete("{keyName}", Delete);

        return app;
    }

    private static async Task<IResult> ListAll(
        IPortalCredentialStore store,
        CancellationToken ct)
    {
        var items = await store.ListAsync(ct);
        return Results.Ok(items.Select(i => new { i.KeyName, i.UpdatedOn }));
    }

    private static async Task<IResult> Upsert(
        string keyName,
        UpsertCredentialRequest request,
        IPortalCredentialStore store,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
            return Results.BadRequest(new { error = "Value is required." });

        await store.SetAsync(keyName, request.Value, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(
        string keyName,
        IPortalCredentialStore store,
        CancellationToken ct)
    {
        await store.DeleteAsync(keyName, ct);
        return Results.NoContent();
    }
}

public record UpsertCredentialRequest(string Value);
