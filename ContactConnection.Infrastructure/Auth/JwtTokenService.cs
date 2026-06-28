using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ContactConnection.Infrastructure.Auth;

public class JwtTokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration) => _configuration = configuration;

    public string GeneratePreAuthToken(Agent agent, Tenant tenant)
    {
        var signingKey = _configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenant.Id.ToString()),
            new Claim("tenant_schema", tenant.SchemaName),
            new Claim("tenant_subdomain", tenant.Subdomain),
            new Claim("role", "mfa_pending")
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "contactconnection",
            audience: _configuration["Jwt:Audience"] ?? "contactconnection-api",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateToken(Agent agent, Tenant tenant, Role? role = null)
    {
        var signingKey = _configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

        var issuer = _configuration["Jwt:Issuer"] ?? "contactconnection";
        var audience = _configuration["Jwt:Audience"] ?? "contactconnection-api";
        var expiryMinutes = int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var m) ? m : 480;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var roleName    = role?.Name ?? agent.Role;
        var permissions = role?.Permissions ?? Domain.Entities.Permission.ForLegacyRole(agent.Role).ToList();
        var landingPage = role?.DefaultLandingPage ?? Domain.Entities.LandingPage.ForLegacyRole(agent.Role);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, agent.Email),
            new Claim(JwtRegisteredClaimNames.GivenName, agent.FirstName),
            new Claim(JwtRegisteredClaimNames.FamilyName, agent.LastName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenant.Id.ToString()),
            new Claim("tenant_schema", tenant.SchemaName),
            new Claim("tenant_subdomain", tenant.Subdomain),
            new Claim("role", roleName),
            new Claim("role_id", role?.Id.ToString() ?? string.Empty),
            new Claim("permissions", string.Join(",", permissions)),
            new Claim("landing_page", landingPage)
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
