namespace ContactConnection.Application.Interfaces.Services;

public interface IPlatformTokenService
{
    string GenerateToken(EntraIdentity identity);
}
