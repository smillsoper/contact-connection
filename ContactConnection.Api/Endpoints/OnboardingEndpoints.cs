using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects;
using ContactConnection.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Api.Endpoints;

public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/onboarding");

        group.MapGet("{token}", ValidateToken);
        group.MapPost("{token}/complete", Complete);

        return app;
    }

    private static async Task<IResult> ValidateToken(
        string token,
        ITenantInviteRepository invites,
        CancellationToken ct)
    {
        var invite = await invites.GetByTokenAsync(token, ct);
        if (invite is null || invite.IsAccepted || invite.IsExpired)
            return Results.NotFound(new { error = "This invitation link is invalid or has expired." });

        return Results.Ok(new
        {
            tenantId = invite.TenantId,
            tenantName = invite.Tenant.Name,
            timezone = invite.Tenant.Timezone,
            email = invite.Email,
            expiresAt = invite.ExpiresAt,
            featureFlags = invite.Tenant.FeatureFlags,
        });
    }

    private static async Task<IResult> Complete(
        string token,
        CompleteOnboardingRequest request,
        ITenantInviteRepository invites,
        ITenantAdminInviteRepository agentInvites,
        IAgentRepository agents,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        TenantContext tenantContext,
        IEmailService email,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var invite = await invites.GetByTokenAsync(token, ct);
        if (invite is null || invite.IsAccepted || invite.IsExpired)
            return Results.NotFound(new { error = "This invitation link is invalid or has expired." });

        var tenant = invite.Tenant;

        // Populate TenantContext so tenant-scoped repositories (AgentRepository) work correctly
        tenantContext.Current = tenant;

        // Apply wizard settings
        tenant.SetDisplayName(string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName);
        tenant.SetLogoUrl(string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl);
        tenant.UpdateSettings(new TenantSettings
        {
            DateFormat = request.DateFormat,
            TimeFormat = request.TimeFormat,
            SupportEmail = request.SupportEmail,
            BillingEmail = request.BillingEmail,
            SessionTimeoutMinutes = request.SessionTimeoutMinutes,
            MfaRequirement = request.MfaRequirement,
        });
        tenant.CompleteOnboarding();
        if (request.FeatureFlags is not null)
            tenant.UpdateFeatureFlags(request.FeatureFlags);

        // Create the first admin agent
        var passwordHash = passwordHasher.Hash(request.Password);
        var admin = Agent.Create(
            tenant.Id,
            request.FirstName,
            request.LastName,
            invite.Email,
            passwordHash,
            AgentRole.Admin);

        await agents.AddAsync(admin, ct);
        await agents.SaveChangesAsync(ct);

        // Create invite records for additional admins
        var logger = loggerFactory.CreateLogger("Onboarding");
        var additionalEmails = request.AdditionalAdminEmails
            ?.Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .Distinct()
            .Where(e => !string.Equals(e, invite.Email, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        foreach (var adminEmail in additionalEmails)
        {
            var agentInvite = TenantAdminInvite.Create(tenant.Id, adminEmail, AgentRole.Admin);
            await agentInvites.AddAsync(agentInvite, ct);

            try
            {
                var baseUrl = configuration["App:BaseUrl"] ?? "https://contactconnection.cc";
                var acceptUrl = $"{baseUrl}/admin-invite/{agentInvite.Token}";
                await email.SendAsync(
                    adminEmail,
                    TenantAdminInviteEmail.Subject(tenant.Name),
                    TenantAdminInviteEmail.HtmlBody(tenant.Name, tenant.DisplayName, tenant.Subdomain, acceptUrl),
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send agent invite email to {Email} for tenant {TenantId}",
                    adminEmail, tenant.Id);
            }
        }

        // Mark tenant invite accepted + save everything
        invite.Accept();
        await invites.SaveChangesAsync(ct);

        admin.RecordLogin();
        var jwtToken = tokenService.GenerateToken(admin, tenant);

        return Results.Ok(new
        {
            token = jwtToken,
            agent = new
            {
                admin.Id,
                admin.FirstName,
                admin.LastName,
                admin.Email,
                admin.Role,
                tenantId = tenant.Id,
                tenantSubdomain = tenant.Subdomain,
            }
        });
    }
}

public record CompleteOnboardingRequest(
    // Account
    string FirstName,
    string LastName,
    string Password,
    // Identity & Branding
    string? DisplayName,
    string? LogoUrl,
    // Locale & Regional
    string DateFormat,
    string TimeFormat,
    // Communication
    string? SupportEmail,
    string? BillingEmail,
    // Security
    int SessionTimeoutMinutes,
    string MfaRequirement,
    // Feature flags
    TenantFeatureFlags? FeatureFlags,
    // Additional admins
    List<string>? AdditionalAdminEmails);
