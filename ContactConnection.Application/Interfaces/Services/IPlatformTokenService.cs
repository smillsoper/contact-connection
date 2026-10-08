namespace ContactConnection.Application.Interfaces.Services;

public interface IPlatformTokenService
{
    /// <param name="platformRole">owner or support (S184, <c>PlatformRole</c>).</param>
    string GenerateToken(EntraIdentity identity, string platformRole);
}
