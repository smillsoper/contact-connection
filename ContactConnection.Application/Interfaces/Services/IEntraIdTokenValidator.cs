namespace ContactConnection.Application.Interfaces.Services;

public record EntraIdentity(string Oid, string Email, string FirstName, string LastName);

public interface IEntraIdTokenValidator
{
    /// <summary>
    /// Validates a Microsoft Entra ID ID token and extracts the user's identity.
    /// Throws if the token is invalid, expired, or issued for the wrong audience.
    /// </summary>
    Task<EntraIdentity> ValidateIdTokenAsync(string idToken, CancellationToken ct = default);
}
