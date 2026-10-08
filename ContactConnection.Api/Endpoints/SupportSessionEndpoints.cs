using System.Security.Cryptography;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Auth;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// ContactConnection support inside a tenant's portal (S184). From Manage Tenant, a Portal user (Owner or Support)
/// gives a reason and opens the tenant's portal as their own named support account ("First Last (ContactConnection
/// Support)") with every permission — except card-data exports while the tenant's switch is off. The hand-over uses a
/// single-use 60-second code (no token in a URL); a session lasts 60 minutes and can be ended from either side. Every
/// session is logged, and the tenant's admins see their own on the Support Access page.
/// </summary>
public static class SupportSessionEndpoints
{
    private static string CodeKey(string code) => $"support-code:{code}";

    public static IEndpointRouteBuilder MapSupportSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var portal = app.MapGroup("/api/v1/portal").RequireAuthorization("PlatformAdmin");
        portal.MapPost("tenants/{id:guid}/support-sessions", Start);
        portal.MapGet("tenants/{id:guid}/support-sessions", PortalList);
        portal.MapPost("support-sessions/{sessionId:guid}/end", PortalEnd);

        app.MapPost("/api/v1/auth/support-login", Login).AllowAnonymous();
        app.MapPost("/api/v1/auth/support-end", TenantEnd).RequireAuthorization();
        app.MapGet("/api/v1/admin/support-access", TenantList).RequireAuthorization("TenantAdmin");
        return app;
    }

    private static object Dto(SupportSession s, DateTimeOffset now) => new
    {
        s.Id, s.Name, s.Email, s.PlatformRole, s.Reason, s.StartedAt, s.ExpiresAt,
        endedAt = s.EndedAt ?? (s.IsActive(now) ? null : s.ExpiresAt),
        active = s.IsActive(now),
    };

    // ── Portal ───────────────────────────────────────────────────────────────────────────────────────────────────

    public record StartRequest(string? Reason);

    private static async Task<IResult> Start(Guid id, StartRequest req, HttpContext http, ContactConnectionDbContext master,
        ITenantDbContextFactory tenantDbs, IConnectionMultiplexer redis, CancellationToken ct)
    {
        var u = http.User;
        var oid = u.FindFirst("sub")?.Value;
        var email = u.FindFirst("email")?.Value ?? "";
        var first = u.FindFirst("given_name")?.Value ?? "";
        var last = u.FindFirst("family_name")?.Value ?? "";
        var role = u.FindFirst("platform_role")?.Value ?? PlatformRole.Owner;
        if (string.IsNullOrEmpty(oid)) return Results.Unauthorized();

        var tenant = await master.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return Results.NotFound();

        // The support person's own account in this tenant — created the first time, renamed to match Entra after that.
        await using var db = tenantDbs.Create(tenant.SchemaName);
        var agent = await db.Agents.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.IsPlatformSupport && a.PlatformSupportOid == oid, ct);
        if (agent is null)
        {
            agent = Agent.CreatePlatformSupport(tenant.Id, oid, first, last);
            db.Agents.Add(agent);
        }
        else
        {
            agent.NameSupport(first, last);
            agent.Activate();
        }
        await db.SaveChangesAsync(ct);

        SupportSession session;
        try
        {
            session = SupportSession.Start(tenant.Id, oid, email, $"{first} {last}".Trim(), role, req.Reason ?? "", agent.Id, DateTimeOffset.UtcNow);
        }
        catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        master.SupportSessions.Add(session);
        await master.SaveChangesAsync(ct);

        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await redis.GetDatabase().StringSetAsync(CodeKey(code), session.Id.ToString(), TimeSpan.FromSeconds(60));
        return Results.Ok(new { sessionId = session.Id, code, subdomain = tenant.Subdomain, expiresAt = session.ExpiresAt });
    }

    private static async Task<IResult> PortalList(Guid id, ContactConnectionDbContext master, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await master.SupportSessions.AsNoTracking().Where(s => s.TenantId == id).OrderByDescending(s => s.StartedAt).Take(100).ToListAsync(ct);
        return Results.Ok(rows.Select(s => Dto(s, now)));
    }

    /// <summary>The person who started it, or the Owner, ends a session from the Portal.</summary>
    private static async Task<IResult> PortalEnd(Guid sessionId, HttpContext http, ContactConnectionDbContext master, IMemoryCache cache, CancellationToken ct)
    {
        var s = await master.SupportSessions.FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (s is null) return Results.NotFound();
        if (s.EntraOid != http.User.FindFirst("sub")?.Value && !await PortalTenantsEndpoints.IsOwnerAsync(http)) return Results.Forbid();
        s.End(DateTimeOffset.UtcNow);
        await master.SaveChangesAsync(ct);
        SupportSessionReader.Forget(cache, s.Id);
        return Results.NoContent();
    }

    // ── Tenant portal ────────────────────────────────────────────────────────────────────────────────────────────

    public record SupportLoginRequest(string? Code);

    /// <summary>Exchanges the single-use code for the support session's token (the tenant portal's /support-login page).</summary>
    private static async Task<IResult> Login(SupportLoginRequest req, TenantContext tc, ContactConnectionDbContext master,
        ITenantDbContextFactory tenantDbs, ITokenService tokens, IConnectionMultiplexer redis, CancellationToken ct)
    {
        if (tc.Current is not { } tenant || string.IsNullOrWhiteSpace(req.Code)) return Results.Unauthorized();
        var value = await redis.GetDatabase().StringGetDeleteAsync(CodeKey(req.Code.Trim()));
        if (value.IsNullOrEmpty || !Guid.TryParse(value.ToString(), out var sessionId))
            return Results.Json(new { error = "This support link has expired — open the tenant's portal again from Manage Tenant." }, statusCode: 401);
        var s = await master.SupportSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (s is null || s.TenantId != tenant.Id || !s.IsActive(DateTimeOffset.UtcNow)) return Results.Unauthorized();

        await using var db = tenantDbs.Create(tenant.SchemaName);
        var agent = await db.Agents.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == s.AgentId && a.IsPlatformSupport, ct);
        if (agent is null) return Results.Unauthorized();
        var permissions = Permission.ForPlatformSupport(tenant.FeatureFlags.CardDataExports);
        var token = tokens.GenerateSupportToken(agent, tenant, permissions, s.Id, s.ExpiresAt);
        return Results.Ok(new
        {
            response = new LoginResponse(false, token, agent.Id, agent.Email, agent.FirstName, agent.LastName, "Administrator",
                tenant.Subdomain, null, null, null, null, permissions.ToArray(), LandingPage.AdminDashboard),
            supportSession = new { s.Id, s.ExpiresAt, tenantName = tenant.DisplayName ?? tenant.Name },
        });
    }

    /// <summary>The End button on the support banner.</summary>
    private static async Task<IResult> TenantEnd(HttpContext http, ContactConnectionDbContext master, IMemoryCache cache, CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirst("support_session")?.Value, out var sessionId)) return Results.BadRequest(new { error = "Not a support session." });
        var s = await master.SupportSessions.FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (s is null) return Results.NotFound();
        s.End(DateTimeOffset.UtcNow);
        await master.SaveChangesAsync(ct);
        SupportSessionReader.Forget(cache, s.Id);
        return Results.NoContent();
    }

    /// <summary>The tenant's own Support Access log: every time ContactConnection support was in this portal.</summary>
    private static async Task<IResult> TenantList(TenantContext tc, ContactConnectionDbContext master, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        var now = DateTimeOffset.UtcNow;
        var rows = await master.SupportSessions.AsNoTracking().Where(s => s.TenantId == tenant.Id).OrderByDescending(s => s.StartedAt).Take(200).ToListAsync(ct);
        return Results.Ok(rows.Select(s => new
        {
            s.Id, s.Name, s.Reason, s.StartedAt,
            endedAt = s.EndedAt ?? (s.IsActive(now) ? null : s.ExpiresAt),
            active = s.IsActive(now),
        }));
    }
}
