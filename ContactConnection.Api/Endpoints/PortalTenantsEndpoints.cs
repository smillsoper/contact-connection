using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Api.Endpoints;

public static class PortalTenantsEndpoints
{
    public static IEndpointRouteBuilder MapPortalTenantsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/portal/tenants")
            .RequireAuthorization("PlatformAdmin");

        group.MapGet("", List);
        group.MapPost("", Provision);
        group.MapGet("{id:guid}", GetById);
        group.MapPatch("{id:guid}", Update);
        group.MapPatch("{id:guid}/feature-flags", UpdateFeatureFlags);
        group.MapPost("{id:guid}/activate", Activate);
        group.MapPost("{id:guid}/deactivate", Deactivate);
        group.MapPost("{id:guid}/resend-invite", ResendInvite);
        group.MapPost("{id:guid}/reset-onboarding", ResetOnboarding);
        group.MapPost("{id:guid}/invite-admin", InviteAdmin);
        group.MapGet("{id:guid}/agents", ListTenantAgents);
        group.MapPost("{id:guid}/agents/{agentId:guid}/reset-password", ResetTenantAgentPassword);

        return app;
    }

    private static async Task<IResult> List(
        ITenantRepository tenants,
        CancellationToken ct)
    {
        var all = await tenants.GetAllAsync(ct);
        return Results.Ok(all.Select(ToResponse));
    }

    private static async Task<IResult> GetById(
        Guid id,
        ITenantRepository tenants,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        return tenant is null ? Results.NotFound() : Results.Ok(ToResponse(tenant));
    }

    private static async Task<IResult> Provision(
        PortalProvisionTenantRequest request,
        ITenantProvisioningService provisioning,
        IEmailService email,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("PortalProvisioning");
        try
        {
            var (tenant, inviteToken) = await provisioning.ProvisionAsync(
                request.Name,
                request.Subdomain,
                request.Timezone,
                request.FeatureFlags,
                request.InviteEmail,
                ct);

            if (!string.IsNullOrWhiteSpace(tenant.InviteEmail) && inviteToken is not null)
            {
                try
                {
                    var baseUrl = configuration["App:BaseUrl"] ?? "https://contactconnection.cc";
                    var onboardingUrl = $"{baseUrl}/onboarding/{inviteToken}";
                    var loginUrl = $"https://{tenant.Subdomain}.{new Uri(baseUrl).Host}/login";
                    await email.SendAsync(
                        tenant.InviteEmail,
                        TenantInviteEmail.Subject(tenant.Name),
                        TenantInviteEmail.HtmlBody(tenant.Name, tenant.Subdomain, onboardingUrl, loginUrl),
                        ct);
                }
                catch (Exception ex)
                {
                    // Email failure must not roll back a successful provisioning
                    logger.LogError(ex, "Failed to send invite email to {Email} for tenant {TenantId}",
                        tenant.InviteEmail, tenant.Id);
                }
            }

            return Results.Created($"/api/v1/portal/tenants/{tenant.Id}", ToResponse(tenant));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateTenantRequest request,
        ITenantRepository tenants,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null) return Results.NotFound();

        if (request.BillingContact is not null)
            tenant.SetBillingContact(request.BillingContact);
        if (request.InviteEmail is not null)
            tenant.SetInviteEmail(request.InviteEmail == "" ? null : request.InviteEmail);
        if (request.CustomDomain is not null)
            tenant.SetCustomDomain(request.CustomDomain == "" ? null : request.CustomDomain);
        if (request.TrialExpiresAt.HasValue)
            tenant.SetTrialExpiry(request.TrialExpiresAt.Value == DateTimeOffset.MinValue ? null : request.TrialExpiresAt.Value);

        await tenants.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(tenant));
    }

    private static async Task<IResult> UpdateFeatureFlags(
        Guid id,
        TenantFeatureFlags flags,
        ITenantRepository tenants,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null) return Results.NotFound();

        tenant.UpdateFeatureFlags(flags);
        await tenants.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(tenant));
    }

    private static async Task<IResult> Activate(
        Guid id,
        ITenantRepository tenants,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null) return Results.NotFound();

        tenant.Activate();
        await tenants.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(tenant));
    }

    private static async Task<IResult> Deactivate(
        Guid id,
        ITenantRepository tenants,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null) return Results.NotFound();

        tenant.Deactivate();
        await tenants.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(tenant));
    }

    private static async Task<IResult> ResendInvite(
        Guid id,
        ITenantRepository tenants,
        ITenantInviteRepository invites,
        IEmailService email,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null) return Results.NotFound();

        if (tenant.OnboardingComplete)
            return Results.BadRequest(new { error = "Onboarding is already complete for this tenant." });

        if (string.IsNullOrWhiteSpace(tenant.InviteEmail))
            return Results.BadRequest(new { error = "No invite email address is set for this tenant." });

        var invite = TenantInvite.Create(tenant.Id, tenant.InviteEmail);
        await invites.AddAsync(invite, ct);
        await invites.SaveChangesAsync(ct);

        try
        {
            var baseUrl = configuration["App:BaseUrl"] ?? "https://contactconnection.cc";
            var onboardingUrl = $"{baseUrl}/onboarding/{invite.Token}";
            var loginUrl = $"https://{tenant.Subdomain}.{new Uri(baseUrl).Host}/login";
            await email.SendAsync(
                tenant.InviteEmail,
                TenantInviteEmail.Subject(tenant.Name),
                TenantInviteEmail.HtmlBody(tenant.Name, tenant.Subdomain, onboardingUrl, loginUrl),
                ct);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("PortalProvisioning")
                .LogError(ex, "Failed to resend invite email to {Email} for tenant {TenantId}",
                    tenant.InviteEmail, tenant.Id);
            return Results.Problem("Invite record created but email delivery failed. Check logs.");
        }

        return Results.Ok(new { message = "Invitation resent." });
    }

    private static async Task<IResult> ResetOnboarding(
        Guid id,
        ITenantRepository tenants,
        IAgentRepository agents,
        ITenantAdminInviteRepository adminInvites,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null) return Results.NotFound();

        // Delete all agents in the tenant schema so re-onboarding starts clean
        tenantContext.Current = tenant;
        await agents.DeleteAllAsync(ct);

        // Delete all pending admin invites for this tenant
        await adminInvites.DeleteByTenantAsync(tenant.Id, ct);

        tenant.ResetOnboarding();
        await tenants.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(tenant));
    }

    private static async Task<IResult> InviteAdmin(
        Guid id,
        InviteAdminRequest request,
        ITenantRepository tenants,
        ITenantAdminInviteRepository adminInvites,
        IEmailService email,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null) return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.BadRequest(new { error = "Email is required." });

        var invite = TenantAdminInvite.Create(tenant.Id, request.Email.Trim().ToLowerInvariant(), AgentRole.Admin);
        await adminInvites.AddAsync(invite, ct);
        await adminInvites.SaveChangesAsync(ct);

        try
        {
            var baseUrl = configuration["App:BaseUrl"] ?? "https://contactconnection.cc";
            var acceptUrl = $"{baseUrl}/admin-invite/{invite.Token}";
            var loginUrl = $"https://{tenant.Subdomain}.{new Uri(baseUrl).Host}/login";
            await email.SendAsync(
                invite.Email,
                TenantAdminInviteEmail.Subject(tenant.Name),
                TenantAdminInviteEmail.HtmlBody(tenant.Name, tenant.DisplayName, tenant.Subdomain, acceptUrl, loginUrl),
                ct);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("PortalAdmin")
                .LogError(ex, "Failed to send admin invite email to {Email} for tenant {TenantId}", invite.Email, tenant.Id);
            return Results.Problem("Invite created but email delivery failed. Check logs.");
        }

        return Results.Ok(new { message = $"Invitation sent to {invite.Email}." });
    }

    private static async Task<IResult> ListTenantAgents(
        Guid id,
        ITenantRepository tenants,
        IAgentRepository agents,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null) return Results.NotFound();

        tenantContext.Current = tenant;
        var all = await agents.GetAllAsync(ct);
        return Results.Ok(all.Select(a => new
        {
            a.Id,
            a.FirstName,
            a.LastName,
            a.Email,
            a.Role,
            a.IsActive,
            a.LastLoginAt,
        }));
    }

    private static async Task<IResult> ResetTenantAgentPassword(
        Guid id,
        Guid agentId,
        ResetTenantAgentPasswordRequest request,
        ITenantRepository tenants,
        IAgentRepository agents,
        IPasswordHasher hasher,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null) return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return Results.BadRequest(new { error = "Password must be at least 8 characters." });

        tenantContext.Current = tenant;
        var agent = await agents.GetByIdAsync(agentId, ct);
        if (agent is null) return Results.NotFound();

        agent.UpdatePasswordHash(hasher.Hash(request.NewPassword));
        await agents.SaveChangesAsync(ct);

        return Results.Ok(new { message = $"Password reset for {agent.Email}." });
    }

    private static object ToResponse(Tenant t) => new
    {
        t.Id,
        t.Name,
        t.DisplayName,
        t.LogoUrl,
        t.Subdomain,
        t.CustomDomain,
        t.SchemaName,
        t.Timezone,
        t.IsActive,
        t.OnboardingComplete,
        t.TrialExpiresAt,
        t.BillingContact,
        t.InviteEmail,
        t.FeatureFlags,
        t.Settings,
        t.CreatedAt
    };
}

public record PortalProvisionTenantRequest(
    string Name,
    string Subdomain,
    string Timezone,
    TenantFeatureFlags? FeatureFlags,
    string? InviteEmail);

public record UpdateTenantRequest(
    string? BillingContact,
    string? InviteEmail,
    string? CustomDomain,
    DateTimeOffset? TrialExpiresAt);

public record ResetTenantAgentPasswordRequest(string NewPassword);
public record InviteAdminRequest(string Email);
