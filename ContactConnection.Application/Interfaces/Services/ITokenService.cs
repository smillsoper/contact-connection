using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Services;

public interface ITokenService
{
    string GenerateToken(Agent agent, Tenant tenant, Role? role = null);
    string GeneratePreAuthToken(Agent agent, Tenant tenant);

    /// <summary>
    /// Client-portal token (S181). Its own audience (<c>{Jwt:Audience}-client</c>) so the agent / admin bearer scheme
    /// rejects it outright; no permissions claim. <paramref name="mfaPending"/>: a 5-minute token good only for the MFA step.
    /// </summary>
    string GenerateClientUserToken(ClientUser user, Tenant tenant, bool mfaPending = false);

    /// <summary>
    /// A ContactConnection support session's token (S184): the support person's account in the tenant, the given
    /// permissions, a <c>support_session</c> claim, and an expiry no later than the session's.
    /// </summary>
    string GenerateSupportToken(Agent agent, Tenant tenant, IReadOnlyList<string> permissions, Guid supportSessionId, DateTimeOffset expiresAt);
}
