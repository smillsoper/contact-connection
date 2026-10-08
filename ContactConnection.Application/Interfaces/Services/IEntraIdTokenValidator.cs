namespace ContactConnection.Application.Interfaces.Services;

/// <param name="Roles">The app roles assigned to the person on the Portal's app registration (S184: Platform.Owner,
/// Platform.Support).</param>
public record EntraIdentity(string Oid, string Email, string FirstName, string LastName, IReadOnlyList<string>? Roles = null);

public interface IEntraIdTokenValidator
{
    /// <summary>
    /// Validates a Microsoft Entra ID ID token and extracts the user's identity.
    /// Throws if the token is invalid, expired, or issued for the wrong audience.
    /// </summary>
    Task<EntraIdentity> ValidateIdTokenAsync(string idToken, CancellationToken ct = default);
}
