using System.IdentityModel.Tokens.Jwt;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ContactConnection.Infrastructure.Auth;

public class EntraIdTokenValidator : IEntraIdTokenValidator
{
    private readonly string _tenantId;
    private readonly string _clientId;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager;

    public EntraIdTokenValidator(IConfiguration configuration)
    {
        _tenantId = configuration["EntraId:TenantId"]
            ?? throw new InvalidOperationException("EntraId:TenantId is not configured.");
        _clientId = configuration["EntraId:ClientId"]
            ?? throw new InvalidOperationException("EntraId:ClientId is not configured.");

        var metadataAddress =
            $"https://login.microsoftonline.com/{_tenantId}/v2.0/.well-known/openid-configuration";

        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());
    }

    public async Task<EntraIdentity> ValidateIdTokenAsync(string idToken, CancellationToken ct = default)
    {
        var oidcConfig = await _configManager.GetConfigurationAsync(ct);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://login.microsoftonline.com/{_tenantId}/v2.0",
            ValidateAudience = true,
            ValidAudience = _clientId,
            ValidateLifetime = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(5),
        };

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(idToken, validationParameters, out _);

        // oid is the stable object ID for the user in the Entra tenant
        var oid = principal.FindFirst("oid")?.Value
            ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? Guid.NewGuid().ToString();

        // preferred_username is the UPN (user@domain.com) — always present for org accounts
        var email =
            principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst("email")?.Value
            ?? principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? throw new SecurityTokenException("No email claim found in Entra ID token.");

        var firstName = principal.FindFirst(JwtRegisteredClaimNames.GivenName)?.Value ?? string.Empty;
        var lastName  = principal.FindFirst(JwtRegisteredClaimNames.FamilyName)?.Value ?? string.Empty;

        return new EntraIdentity(oid, email.ToLowerInvariant(), firstName, lastName);
    }
}
