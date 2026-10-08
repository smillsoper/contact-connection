using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
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
        IConfiguration configuration,
        ContactConnection.Infrastructure.Data.ContactConnectionDbContext db,
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

        // S184: the Portal role comes from the app roles on the Portal's Entra app registration. Until
        // PlatformAuth:EnforceRoles is on, a sign-in with no role is treated as Owner (no lock-out while roles are set up).
        var role = PlatformRole.Resolve(identity.Roles ?? [], configuration.GetValue<bool>("PlatformAuth:EnforceRoles"));
        if (role is null)
            return Results.Json(new { error = "Your account has no ContactConnection Portal role. Ask the account owner to assign one." },
                statusCode: StatusCodes.Status403Forbidden);

        // S184: remember who signs in to the Portal and with what role — Owners here get platform health alerts.
        var name = $"{identity.FirstName} {identity.LastName}".Trim();
        var seen = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.PlatformUsers, u => u.EntraOid == identity.Oid, ct);
        if (seen is null) db.PlatformUsers.Add(PlatformUser.Seen(identity.Oid, identity.Email, name, role, DateTimeOffset.UtcNow));
        else seen.SeenAgain(identity.Email, name, role, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        var token = tokens.GenerateToken(identity, role);

        return Results.Ok(new PortalLoginResponse(
            token,
            identity.Oid,
            identity.Email,
            identity.FirstName,
            identity.LastName,
            role));
    }
}

public record EntraLoginRequest(string IdToken);

public record PortalLoginResponse(
    string Token,
    string AdminId,
    string Email,
    string FirstName,
    string LastName,
    string PlatformRole);
