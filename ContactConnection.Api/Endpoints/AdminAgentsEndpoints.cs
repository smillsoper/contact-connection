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
        const string prefix = "/api/v1/admin/agents";

        app.MapGet(prefix,                                   List)           .RequireAuthorization("AgentsView");
        app.MapPost($"{prefix}/invite",                      InviteUsers)    .RequireAuthorization("TenantAdmin");
        app.MapPost($"{prefix}/{{id:guid}}/reset-password",  ResetPassword)  .RequireAuthorization("TenantAdmin");
        app.MapPatch($"{prefix}/{{id:guid}}",                Update)         .RequireAuthorization("TenantAdmin");

        return app;
    }

    private static async Task<IResult> InviteUsers(
        InviteUsersRequest request,
        ITenantAdminInviteRepository invites,
        IRoleRepository roles,
        IEmailService email,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();
        var tenant = tenantContext.Current!;

        // Parse emails from comma/newline/semicolon delimited input
        var emails = (request.Emails ?? "")
            .Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => e.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (emails.Count == 0)
            return Results.BadRequest(new { error = "At least one valid email address is required." });

        // Resolve role
        Role? role = null;
        if (request.RoleId.HasValue)
            role = await roles.GetByIdAsync(request.RoleId.Value, ct);

        var legacyRole = role?.Name switch
        {
            "Administrator" => AgentRole.Admin,
            "Supervisor"    => AgentRole.Supervisor,
            _               => AgentRole.Agent,
        };
        var roleName = role?.Name ?? "Agent";

        var baseUrl = configuration["App:BaseUrl"] ?? "https://contactconnection.io";
        var logger = loggerFactory.CreateLogger("AdminAgents");
        var sent = new List<string>();
        var failed = new List<string>();

        foreach (var emailAddress in emails)
        {
            var invite = TenantAdminInvite.Create(
                tenant.Id, emailAddress, legacyRole,
                roleId: role?.Id, roleName: roleName);
            await invites.AddAsync(invite, ct);
            await invites.SaveChangesAsync(ct);

            try
            {
                var acceptUrl = $"{baseUrl}/admin-invite/{invite.Token}";
                var loginUrl = $"https://{tenant.Subdomain}.{new Uri(baseUrl).Host}/login";
                await email.SendAsync(
                    invite.Email,
                    TenantAdminInviteEmail.Subject(tenant.Name, roleName),
                    TenantAdminInviteEmail.HtmlBody(tenant.Name, tenant.DisplayName, tenant.Subdomain, acceptUrl, roleName, loginUrl),
                    ct);
                sent.Add(emailAddress);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send invite to {Email} for tenant {TenantId}", emailAddress, tenant.Id);
                failed.Add(emailAddress);
            }
        }

        return Results.Ok(new
        {
            sent = sent.Count,
            failed,
            message = failed.Count == 0
                ? $"{sent.Count} invitation{(sent.Count != 1 ? "s" : "")} sent."
                : $"{sent.Count} sent, {failed.Count} failed.",
        });
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
        IRoleRepository roles,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant) return Results.Unauthorized();

        var agent = await agents.GetByIdAsync(id, ct);
        if (agent is null) return Results.NotFound();

        // Custom role assignment takes precedence over legacy role string
        if (request.RoleId is not null)
        {
            if (request.RoleId == Guid.Empty)
            {
                agent.SetCustomRole(null);
            }
            else
            {
                var role = await roles.GetByIdAsync(request.RoleId.Value, ct);
                if (role is null) return Results.BadRequest(new { error = "Role not found." });
                agent.SetCustomRole(role.Id);
            }
        }
        else if (request.Role is not null)
        {
            if (!AgentRole.IsValid(request.Role))
                return Results.BadRequest(new { error = $"Invalid role '{request.Role}'." });
            agent.SetRole(request.Role);
        }

        if (request.IsActive is true) agent.Activate();
        else if (request.IsActive is false) agent.Deactivate();

        if (request.Timezone is not null)
            agent.SetTimezone(string.IsNullOrWhiteSpace(request.Timezone) ? null : request.Timezone);

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
        RoleId = a.RoleId,
        RoleName = a.CustomRole?.Name,
        a.IsActive,
        a.CreatedAt,
        a.LastLoginAt,
        a.Timezone,
    };
}

public record ResetAgentPasswordRequest(string NewPassword);
public record UpdateAgentRequest(string? Role, Guid? RoleId, bool? IsActive, string? Timezone);
public record InviteUsersRequest(string? Emails, Guid? RoleId);
