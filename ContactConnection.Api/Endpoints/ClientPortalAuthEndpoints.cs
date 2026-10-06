using System.Security.Claims;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Client-portal sign-in (S181, docs/client-dashboards-plan.md §B): invite → set password → sign in (+ MFA per the
/// tenant's MFA setting). Entirely separate from agent auth — its own table, its own token audience. Rate-limited per IP.
/// </summary>
public static class ClientPortalAuthEndpoints
{
    public const int MinPasswordLength = 10;

    public static IEndpointRouteBuilder MapClientPortalAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/client-portal/auth").RequireRateLimiting("client-auth");
        g.MapGet("invite/{token}", Invite);
        g.MapPost("invite/{token}/accept", AcceptInvite);
        g.MapPost("login", Login);
        g.MapGet("mfa/setup", MfaSetup).RequireAuthorization("ClientMfaPending");
        g.MapPost("mfa/setup/confirm", MfaSetupConfirm).RequireAuthorization("ClientMfaPending");
        g.MapPost("mfa/verify", MfaVerify).RequireAuthorization("ClientMfaPending");
        g.MapPost("refresh", Refresh).RequireAuthorization("ClientUser");
        return app;
    }

    private static async Task<ClientUser?> FindByInviteAsync(TenantDbContext db, string token, CancellationToken ct)
    {
        var hash = ClientUser.HashToken(token);
        var user = await db.ClientUsers.Include(u => u.Dashboards).FirstOrDefaultAsync(u => u.InviteTokenHash == hash, ct);
        return user is not null && user.IsActive && user.InviteMatches(token) ? user : null;
    }

    private static async Task<IResult> Invite(string token, ScopedTenantDbContextFactory dbFactory, TenantContext tenantContext, CancellationToken ct)
    {
        if (tenantContext.Current is not { } tenant) return Results.NotFound();
        await using var db = dbFactory.Create();
        var user = await FindByInviteAsync(db, token, ct);
        if (user is null) return Results.NotFound(new { error = "This link is invalid or has expired. Ask for a new one." });
        return Results.Ok(new
        {
            email = user.Email, firstName = user.FirstName, lastName = user.LastName, hasPassword = user.HasPassword,
            tenantName = tenant.DisplayName ?? tenant.Name, tenantLogoUrl = tenant.LogoUrl,
            minPasswordLength = MinPasswordLength,
        });
    }

    private static async Task<IResult> AcceptInvite(string token, AcceptClientInviteRequest req, ScopedTenantDbContextFactory dbFactory,
        TenantContext tenantContext, IPasswordHasher hasher, ITokenService tokens, HttpContext http, CancellationToken ct)
    {
        if (tenantContext.Current is not { } tenant) return Results.NotFound();
        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < MinPasswordLength)
            return Results.BadRequest(new { error = $"Use at least {MinPasswordLength} characters." });

        await using var db = dbFactory.Create();
        var user = await FindByInviteAsync(db, token, ct);
        if (user is null) return Results.NotFound(new { error = "This link is invalid or has expired. Ask for a new one." });

        user.AcceptInvite(hasher.Hash(req.Password), req.FirstName, req.LastName);
        db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.InviteAccepted, null, Ip(http)));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await SignInAsync(db, user, tenant, tokens, http, ct));
    }

    // A real BCrypt hash to verify against when the email is unknown, so both failures take the same time.
    private static string? _dummyHash;

    private static async Task<IResult> Login(ClientLoginRequest req, ScopedTenantDbContextFactory dbFactory, TenantContext tenantContext,
        IPasswordHasher hasher, ITokenService tokens, HttpContext http, CancellationToken ct)
    {
        if (tenantContext.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        var user = await db.ClientUsers.Include(u => u.Dashboards).FirstOrDefaultAsync(u => u.Email == email, ct);

        var ok = user is { PasswordHash: not null } && hasher.Verify(req.Password ?? "", user.PasswordHash);
        if (user is null) hasher.Verify(req.Password ?? "", _dummyHash ??= hasher.Hash(Guid.NewGuid().ToString()));
        // Identical answer for unknown email, wrong password and a deactivated account — no enumeration.
        if (!ok || !user!.IsActive)
        {
            if (user is not null)
            {
                db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.SignInFailed, ok ? "inactive" : "bad password", Ip(http)));
                await db.SaveChangesAsync(ct);
            }
            return Results.Unauthorized();
        }

        return Results.Ok(await SignInAsync(db, user, tenant, tokens, http, ct));
    }

    /// <summary>Either a full session, or — when the tenant requires MFA, or the user turned it on — a 5-minute MFA token.</summary>
    private static async Task<ClientAuthResponse> SignInAsync(TenantDbContext db, ClientUser user, Tenant tenant, ITokenService tokens,
        HttpContext http, CancellationToken ct)
    {
        var requirement = tenant.Settings.MfaRequirement;
        var needsSetup = requirement == "on" && !user.MfaEnabled;
        var needsChallenge = user.MfaEnabled && requirement != "off";
        if (needsSetup || needsChallenge)
            return new ClientAuthResponse(true, needsSetup, tokens.GenerateClientUserToken(user, tenant, mfaPending: true), null, null);
        return await CompleteAsync(db, user, tenant, tokens, http, ct);
    }

    private static async Task<ClientAuthResponse> CompleteAsync(TenantDbContext db, ClientUser user, Tenant tenant, ITokenService tokens,
        HttpContext http, CancellationToken ct)
    {
        user.RecordLogin();
        db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.SignIn, null, Ip(http)));
        await db.SaveChangesAsync(ct);
        return new ClientAuthResponse(false, null, null, tokens.GenerateClientUserToken(user, tenant), Profile(user, tenant));
    }

    private static async Task<IResult> MfaSetup(ClaimsPrincipal principal, ScopedTenantDbContextFactory dbFactory, TenantContext tenantContext,
        IMfaService mfa, IConfiguration config, CancellationToken ct)
    {
        if (tenantContext.Current is null || !TryUserId(principal, out var id)) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var user = await db.ClientUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null || !user.CanSignIn) return Results.Unauthorized();
        if (user.MfaEnabled) return Results.Conflict(new { error = "Two-step sign-in is already set up." });
        var secret = mfa.GenerateSecret();
        user.StoreMfaSecret(secret);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new MfaSetupResponse(secret, mfa.GetOtpAuthUri(secret, user.Email, config["App:IssuerName"] ?? "ContactConnection")));
    }

    private static async Task<IResult> MfaSetupConfirm(MfaCodeRequest req, ClaimsPrincipal principal, ScopedTenantDbContextFactory dbFactory,
        TenantContext tenantContext, IMfaService mfa, ITokenService tokens, HttpContext http, CancellationToken ct)
    {
        if (tenantContext.Current is not { } tenant || !TryUserId(principal, out var id)) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var user = await db.ClientUsers.Include(u => u.Dashboards).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null || !user.CanSignIn || user.MfaEnabled || user.MfaSecret is null) return Results.Unauthorized();
        if (!mfa.Verify(user.MfaSecret, req.Code)) return Results.UnprocessableEntity(new { error = "Invalid code." });
        user.EnableMfa();
        db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.MfaEnabled, null, Ip(http)));
        return Results.Ok(await CompleteAsync(db, user, tenant, tokens, http, ct));
    }

    private static async Task<IResult> MfaVerify(MfaCodeRequest req, ClaimsPrincipal principal, ScopedTenantDbContextFactory dbFactory,
        TenantContext tenantContext, IMfaService mfa, ITokenService tokens, HttpContext http, CancellationToken ct)
    {
        if (tenantContext.Current is not { } tenant || !TryUserId(principal, out var id)) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var user = await db.ClientUsers.Include(u => u.Dashboards).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null || !user.CanSignIn || !user.MfaEnabled || user.MfaSecret is null) return Results.Unauthorized();
        if (!mfa.Verify(user.MfaSecret, req.Code))
        {
            db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.SignInFailed, "bad MFA code", Ip(http)));
            await db.SaveChangesAsync(ct);
            return Results.UnprocessableEntity(new { error = "Invalid code." });
        }
        return Results.Ok(await CompleteAsync(db, user, tenant, tokens, http, ct));
    }

    // "Stay signed in" — a fresh token for a still-active account (the scheme already refused inactive ones).
    private static async Task<IResult> Refresh(ClaimsPrincipal principal, ScopedTenantDbContextFactory dbFactory, TenantContext tenantContext,
        ITokenService tokens, CancellationToken ct)
    {
        if (tenantContext.Current is not { } tenant || !TryUserId(principal, out var id)) return Results.Unauthorized();
        await using var db = dbFactory.Create();
        var user = await db.ClientUsers.AsNoTracking().Include(u => u.Dashboards).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null || !user.CanSignIn) return Results.Unauthorized();
        return Results.Ok(new ClientAuthResponse(false, null, null, tokens.GenerateClientUserToken(user, tenant), Profile(user, tenant)));
    }

    internal static bool TryUserId(ClaimsPrincipal principal, out Guid id) => Guid.TryParse(principal.FindFirst("sub")?.Value, out id);
    internal static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    internal static ClientUserProfile Profile(ClientUser u, Tenant tenant) => new(u.Id, u.Email, u.FirstName, u.LastName, u.CanPlayRecordings,
        u.TimeZone, tenant.Timezone, u.DefaultDashboardId, tenant.DisplayName ?? tenant.Name, tenant.Subdomain, u.MfaEnabled);
}

public record AcceptClientInviteRequest(string? FirstName, string? LastName, string Password);
public record ClientLoginRequest(string? Email, string? Password);
public record ClientUserProfile(Guid Id, string Email, string FirstName, string LastName, bool CanPlayRecordings, string? TimeZone,
    string TenantTimeZone, Guid? DefaultDashboardId, string TenantName, string TenantSubdomain, bool MfaEnabled);
/// <param name="PreAuthToken">MFA step only — good for 5 minutes and only the MFA endpoints.</param>
public record ClientAuthResponse(bool MfaPending, bool? MfaSetupRequired, string? PreAuthToken, string? Token, ClientUserProfile? User);
