using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Email;

namespace ContactConnection.Api.Endpoints;

public static class AdminAgentsEndpoints
{
    public static IEndpointRouteBuilder MapAdminAgentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/agents")
            .RequireAuthorization("TenantAdmin");

        group.MapGet("", List);
        group.MapPost("invite", InviteAdmin);
        group.MapPost("{id:guid}/reset-password", ResetPassword);
        group.MapPatch("{id:guid}", Update);

        return app;
    }

    private static async Task<IResult> InviteAdmin(
        InviteAdminAgentRequest request,
        ITenantAdminInviteRepository invites,
        IEmailService email,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var tenant = tenantContext.Current!;

        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.BadRequest(new { error = "Email is required." });

        var invite = TenantAdminInvite.Create(tenant.Id, request.Email.Trim().ToLowerInvariant(), AgentRole.Admin);
        await invites.AddAsync(invite, ct);
        await invites.SaveChangesAsync(ct);

        try
        {
            var baseUrl = configuration["App:BaseUrl"] ?? "https://contactconnection.cc";
            var acceptUrl = $"{baseUrl}/admin-invite/{invite.Token}";
            await email.SendAsync(
                invite.Email,
                TenantAdminInviteEmail.Subject(tenant.Name),
                TenantAdminInviteEmail.HtmlBody(tenant.Name, tenant.DisplayName, tenant.Subdomain, acceptUrl),
                ct);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("AdminAgents")
                .LogError(ex, "Failed to send admin invite to {Email} for tenant {TenantId}", invite.Email, tenant.Id);
            return Results.Problem("Invite created but email delivery failed. Check logs.");
        }

        return Results.Ok(new { message = "Invitation sent." });
    }

    private static async Task<IResult> List(
        IAgentRepository agents,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var all = await agents.GetAllAsync(ct);
        return Results.Ok(all.Select(ToResponse));
    }

    private static async Task<IResult> ResetPassword(
        Guid id,
        ResetAgentPasswordRequest request,
        IAgentRepository agents,
        IPasswordHasher hasher,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return Results.BadRequest(new { error = "Password must be at least 8 characters." });

        var agent = await agents.GetByIdAsync(id, ct);
        if (agent is null) return Results.NotFound();

        agent.UpdatePasswordHash(hasher.Hash(request.NewPassword));
        await agents.SaveChangesAsync(ct);

        return Results.Ok(ToResponse(agent));
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateAgentRequest request,
        IAgentRepository agents,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var agent = await agents.GetByIdAsync(id, ct);
        if (agent is null) return Results.NotFound();

        if (request.Role is not null)
        {
            if (!AgentRole.IsValid(request.Role))
                return Results.BadRequest(new { error = $"Invalid role '{request.Role}'." });
            agent.SetRole(request.Role);
        }

        if (request.IsActive is true) agent.Activate();
        else if (request.IsActive is false) agent.Deactivate();

        await agents.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(agent));
    }

    private static object ToResponse(Agent a) => new
    {
        a.Id,
        a.FirstName,
        a.LastName,
        a.Email,
        a.Role,
        a.IsActive,
        a.CreatedAt,
        a.LastLoginAt,
    };
}

public record ResetAgentPasswordRequest(string NewPassword);
public record UpdateAgentRequest(string? Role, bool? IsActive);
public record InviteAdminAgentRequest(string Email);
