using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ContactConnection.Api.Endpoints;

/// <summary>Extracts the acting identity from the current request's JWT claims, for version
/// history's CreatedById/CreatedByName (see IVersionHistoryService). Works for both tenant agent
/// JWTs and portal admin JWTs — both use the same claim names (sub/given_name/family_name/email),
/// so one helper covers both call sites.</summary>
internal static class ActorResolver
{
    public static (Guid Id, string Name)? Resolve(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(idClaim, out var id)) return null;

        var first = user.FindFirst(JwtRegisteredClaimNames.GivenName)?.Value ?? "";
        var last  = user.FindFirst(JwtRegisteredClaimNames.FamilyName)?.Value ?? "";
        var name  = string.Join(" ", new[] { first, last }.Where(s => s.Length > 0));
        if (name.Length == 0) name = user.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? "Unknown";

        return (id, name);
    }
}
