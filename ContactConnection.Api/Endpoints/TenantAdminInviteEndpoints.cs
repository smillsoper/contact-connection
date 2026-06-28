using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class TenantAdminInviteEndpoints
{
    public static IEndpointRouteBuilder MapTenantAdminInviteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin-invite");

        group.MapGet("{token}", ValidateToken);
        group.MapPost("{token}/accept", Accept);

        return app;
    }

    private static async Task<IResult> ValidateToken(
        string token,
        ITenantAdminInviteRepository invites,
        CancellationToken ct)
    {
        var invite = await invites.GetByTokenAsync(token, ct);
        if (invite is null || invite.IsAccepted || invite.IsExpired)
            return Results.NotFound(new { error = "This invitation link is invalid or has expired." });

        var tenant = invite.Tenant;
        return Results.Ok(new
        {
            email = invite.Email,
            role = invite.Role,
            tenantId = tenant.Id,
            tenantName = tenant.Name,
            tenantDisplayName = tenant.DisplayName,
            tenantLogoUrl = tenant.LogoUrl,
            mfaRequirement = tenant.Settings.MfaRequirement,
            expiresAt = invite.ExpiresAt,
        });
    }

    private static async Task<IResult> Accept(
        string token,
        AcceptTenantAdminInviteRequest request,
        ITenantAdminInviteRepository invites,
        IAgentRepository agents,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        var invite = await invites.GetByTokenAsync(token, ct);
        if (invite is null || invite.IsAccepted || invite.IsExpired)
            return Results.NotFound(new { error = "This invitation link is invalid or has expired." });

        var tenant = invite.Tenant;

        // Populate TenantContext so AgentRepository can resolve the tenant schema
        tenantContext.Current = tenant;

        var passwordHash = passwordHasher.Hash(request.Password);
        var agent = Agent.Create(
            tenant.Id,
            request.FirstName,
            request.LastName,
            invite.Email,
            passwordHash,
            invite.Role);

        if (invite.RoleId.HasValue)
            agent.SetCustomRole(invite.RoleId.Value);

        await agents.AddAsync(agent, ct);
        await agents.SaveChangesAsync(ct);

        invite.Accept();
        await invites.SaveChangesAsync(ct);

        agent.RecordLogin();
        var jwtToken = tokenService.GenerateToken(agent, tenant);

        return Results.Ok(new
        {
            token = jwtToken,
            agent = new
            {
                agent.Id,
                agent.FirstName,
                agent.LastName,
                agent.Email,
                agent.Role,
                tenantId = tenant.Id,
                tenantSubdomain = tenant.Subdomain,
            }
        });
    }
}

public record AcceptTenantAdminInviteRequest(
    string FirstName,
    string LastName,
    string Password);
