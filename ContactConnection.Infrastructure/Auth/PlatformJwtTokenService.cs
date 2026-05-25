using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ContactConnection.Infrastructure.Auth;

public class PlatformJwtTokenService : IPlatformTokenService
{
    private readonly IConfiguration _configuration;

    public PlatformJwtTokenService(IConfiguration configuration) => _configuration = configuration;

    public string GenerateToken(EntraIdentity identity)
    {
        var signingKey = _configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

        var issuer = _configuration["Jwt:Issuer"] ?? "contactconnection";
        var audience = _configuration["Jwt:Audience"] ?? "contactconnection-api";
        var expiryMinutes = int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var m) ? m : 480;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, identity.Oid),
            new Claim(JwtRegisteredClaimNames.Email, identity.Email),
            new Claim(JwtRegisteredClaimNames.GivenName, identity.FirstName),
            new Claim(JwtRegisteredClaimNames.FamilyName, identity.LastName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("role", "platform_admin")
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
