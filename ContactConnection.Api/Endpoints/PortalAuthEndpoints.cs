using ContactConnection.Application.Interfaces.Services;
using Microsoft.IdentityModel.Tokens;

namespace ContactConnection.Api.Endpoints;

public static class PortalAuthEndpoints
{
    public static IEndpointRouteBuilder MapPortalAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/portal/auth");

        group.MapPost("entra-login", EntraLogin).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> EntraLogin(
        EntraLoginRequest request,
        IEntraIdTokenValidator validator,
        IPlatformTokenService tokens,
        CancellationToken ct)
    {
        EntraIdentity identity;
        try
        {
            identity = await validator.ValidateIdTokenAsync(request.IdToken, ct);
        }
        catch (SecurityTokenException)
        {
            return Results.Unauthorized();
        }

        var token = tokens.GenerateToken(identity);

        return Results.Ok(new PortalLoginResponse(
            token,
            identity.Oid,
            identity.Email,
            identity.FirstName,
            identity.LastName));
    }
}

public record EntraLoginRequest(string IdToken);

public record PortalLoginResponse(
    string Token,
    string AdminId,
    string Email,
    string FirstName,
    string LastName);
