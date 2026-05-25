namespace ContactConnection.Application.Interfaces.Services;

public interface IMfaService
{
    string GenerateSecret();
    string GetOtpAuthUri(string secret, string email, string issuer);
    bool Verify(string secret, string code);
}
