using System.Security.Claims;
using ContactConnection.Application.Helpers;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapPost("login", Login);
        group.MapPost("refresh", Refresh).RequireAuthorization();

        var mfa = group.MapGroup("mfa");
        mfa.MapGet("setup", MfaSetup).RequireAuthorization("MfaPending");
        mfa.MapPost("setup/confirm", MfaSetupConfirm).RequireAuthorization("MfaPending");
        mfa.MapPost("verify", MfaVerify).RequireAuthorization("MfaPending");

        return app;
    }

    private static async Task<IResult> Refresh(
        ClaimsPrincipal user,
        IAgentRepository agents,
        ITokenService tokens,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is null) return Results.Unauthorized();

        var agentIdClaim = user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(agentIdClaim, out var agentId))
            return Results.Unauthorized();

        var agent = await agents.GetByIdAsync(agentId, ct);
        if (agent is null) return Results.Unauthorized();

        var (sipExt, sipPwd) = await RefreshSipCredentialsAsync(agent, tenantContext.Current, agents, ct);
        await agents.SaveChangesAsync(ct);

        var token = tokens.GenerateToken(agent, tenantContext.Current);
        return Results.Ok(BuildFullLoginResponse(token, agent, tenantContext.Current.Subdomain, sipExt, sipPwd));
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        IAgentRepository agents,
        ITenantRepository tenants,
        IPasswordHasher hasher,
        ITokenService tokens,
        IMfaService mfa,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
            return Results.Unauthorized();

        var tenant = tenantContext.Current!;
        var agent = await agents.GetByEmailAsync(request.Email, ct);

        // Identical response for unknown email and wrong password — no enumeration
        if (agent is null || !hasher.Verify(request.Password, agent.PasswordHash))
            return Results.Unauthorized();

        var mfaRequirement = tenant.Settings.MfaRequirement;

        // Determine if MFA challenge is needed
        bool needsChallenge = mfaRequirement == "on" || (mfaRequirement == "optional" && agent.MfaEnabled);
        bool needsSetup = mfaRequirement == "on" && !agent.MfaEnabled;

        if (needsChallenge || needsSetup)
        {
            var preAuthToken = tokens.GeneratePreAuthToken(agent, tenant);
            return Results.Ok(new LoginResponse(
                MfaPending: true,
                Token: null,
                AgentId: agent.Id,
                Email: agent.Email,
                FirstName: null,
                LastName: null,
                Role: null,
                TenantSubdomain: tenant.Subdomain,
                PreAuthToken: preAuthToken,
                MfaSetupRequired: needsSetup,
                SipExtension: null,
                SipPassword: null));
        }

        // Full login — refresh SIP credentials and return the plaintext password once
        var (sipExt, sipPwd) = await RefreshSipCredentialsAsync(agent, tenant, agents, ct);
        agent.RecordLogin();
        await agents.SaveChangesAsync(ct);

        var token = tokens.GenerateToken(agent, tenant);

        return Results.Ok(new LoginResponse(
            MfaPending: false,
            Token: token,
            AgentId: agent.Id,
            Email: agent.Email,
            FirstName: agent.FirstName,
            LastName: agent.LastName,
            Role: agent.Role,
            TenantSubdomain: tenant.Subdomain,
            PreAuthToken: null,
            MfaSetupRequired: null,
            SipExtension: sipExt,
            SipPassword: sipPwd));
    }

    // GET /api/v1/auth/mfa/setup — called when agent needs to configure MFA for the first time
    // Generates a TOTP secret, stores it on the agent (not yet enabled), returns otpauth URI for QR code
    private static async Task<IResult> MfaSetup(
        ClaimsPrincipal user,
        IAgentRepository agents,
        IMfaService mfa,
        TenantContext tenantContext,
        IConfiguration config,
        CancellationToken ct)
    {
        if (!ResolveAgent(user, tenantContext, out var agentId))
            return Results.Unauthorized();

        var agent = await agents.GetByIdAsync(agentId, ct);
        if (agent is null) return Results.Unauthorized();

        var secret = mfa.GenerateSecret();
        agent.StoreMfaSecret(secret);
        await agents.SaveChangesAsync(ct);

        var issuer = config["App:IssuerName"] ?? "ContactConnection";
        var uri = mfa.GetOtpAuthUri(secret, agent.Email, issuer);

        return Results.Ok(new MfaSetupResponse(secret, uri));
    }

    // POST /api/v1/auth/mfa/setup/confirm — verifies TOTP code after scanning QR; enables MFA + issues full JWT
    private static async Task<IResult> MfaSetupConfirm(
        MfaCodeRequest request,
        ClaimsPrincipal user,
        IAgentRepository agents,
        ITokenService tokens,
        IMfaService mfa,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!ResolveAgent(user, tenantContext, out var agentId))
            return Results.Unauthorized();

        var agent = await agents.GetByIdAsync(agentId, ct);
        if (agent is null || agent.MfaSecret is null) return Results.Unauthorized();

        if (!mfa.Verify(agent.MfaSecret, request.Code))
            return Results.UnprocessableEntity(new { error = "Invalid code." });

        agent.EnableMfa();
        var (sipExt1, sipPwd1) = await RefreshSipCredentialsAsync(agent, tenantContext.Current!, agents, ct);
        agent.RecordLogin();
        await agents.SaveChangesAsync(ct);

        var token = tokens.GenerateToken(agent, tenantContext.Current!);
        return Results.Ok(BuildFullLoginResponse(token, agent, tenantContext.Current!.Subdomain, sipExt1, sipPwd1));
    }

    // POST /api/v1/auth/mfa/verify — TOTP challenge for agents who already have MFA enabled
    private static async Task<IResult> MfaVerify(
        MfaCodeRequest request,
        ClaimsPrincipal user,
        IAgentRepository agents,
        ITokenService tokens,
        IMfaService mfa,
        TenantContext tenantContext,
        CancellationToken ct)
    {
        if (!ResolveAgent(user, tenantContext, out var agentId))
            return Results.Unauthorized();

        var agent = await agents.GetByIdAsync(agentId, ct);
        if (agent is null || !agent.MfaEnabled || agent.MfaSecret is null)
            return Results.Unauthorized();

        if (!mfa.Verify(agent.MfaSecret, request.Code))
            return Results.UnprocessableEntity(new { error = "Invalid code." });

        var (sipExt2, sipPwd2) = await RefreshSipCredentialsAsync(agent, tenantContext.Current!, agents, ct);
        agent.RecordLogin();
        await agents.SaveChangesAsync(ct);

        var token = tokens.GenerateToken(agent, tenantContext.Current!);
        return Results.Ok(BuildFullLoginResponse(token, agent, tenantContext.Current!.Subdomain, sipExt2, sipPwd2));
    }

    private static bool ResolveAgent(ClaimsPrincipal user, TenantContext tenantContext, out Guid agentId)
    {
        agentId = Guid.Empty;
        if (tenantContext.Current is null) return false;
        var sub = user.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out agentId);
    }

    private static LoginResponse BuildFullLoginResponse(
        string token, Agent agent, string subdomain,
        string? sipExt = null, string? sipPwd = null) =>
        new(
            MfaPending: false,
            Token: token,
            AgentId: agent.Id,
            Email: agent.Email,
            FirstName: agent.FirstName,
            LastName: agent.LastName,
            Role: agent.Role,
            TenantSubdomain: subdomain,
            PreAuthToken: null,
            MfaSetupRequired: null,
            SipExtension: sipExt,
            SipPassword: sipPwd);

    // Generates a fresh SIP password on every full login.
    // Backfills SipExtension for agents created before this feature.
    // Saves nothing — caller must call agents.SaveChangesAsync().
    private static async Task<(string Extension, string Password)> RefreshSipCredentialsAsync(
        Agent agent,
        Domain.Entities.Tenant tenant,
        IAgentRepository agents,
        CancellationToken ct)
    {
        var extension = agent.SipExtension;
        if (extension is null)
        {
            var maxExt = await agents.GetMaxSipExtensionAsync(ct);
            extension = (maxExt + 1).ToString();
        }

        var password = SipCredentialHelper.GeneratePassword();
        var a1hash = SipCredentialHelper.ComputeA1Hash(extension, tenant.Subdomain, password);
        agent.SetSipCredentials(extension, a1hash);

        return (extension, password);
    }
}

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    bool MfaPending,
    string? Token,
    Guid AgentId,
    string Email,
    string? FirstName,
    string? LastName,
    string? Role,
    string TenantSubdomain,
    string? PreAuthToken,
    bool? MfaSetupRequired,
    string? SipExtension,   // null on MFA-pending responses
    string? SipPassword);   // plaintext, returned on every full login or token refresh — never persisted

public record MfaSetupResponse(string Secret, string OtpAuthUri);

public record MfaCodeRequest(string Code);
