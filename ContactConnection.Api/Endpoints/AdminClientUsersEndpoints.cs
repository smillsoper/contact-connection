using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.ClientPortal;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Tenant admins manage client users (S181): invite, assign client dashboards, recording access, deactivate, resend the
/// set-password link, reset MFA, read the audit trail. Every change is audited with the admin who made it.
/// </summary>
public static class AdminClientUsersEndpoints
{
    public static IEndpointRouteBuilder MapAdminClientUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/admin/client-users").RequireAuthorization("TenantAdmin");
        g.MapGet("", List);
        g.MapPost("", Invite);
        g.MapPatch("{id:guid}", Update);
        g.MapPost("{id:guid}/send-link", SendLink);
        g.MapPost("{id:guid}/reset-mfa", ResetMfa);
        g.MapDelete("{id:guid}", Delete);
        g.MapGet("audit", Audit);
        return app;
    }

    private static object ToResponse(ClientUser u) => new
    {
        id = u.Id, email = u.Email, firstName = u.FirstName, lastName = u.LastName, isActive = u.IsActive,
        canPlayRecordings = u.CanPlayRecordings, mfaEnabled = u.MfaEnabled, hasPassword = u.HasPassword,
        linkPending = u.InviteExpiresAt > DateTimeOffset.UtcNow, linkExpiresAt = u.InviteExpiresAt,
        lastLoginAt = u.LastLoginAt, createdAt = u.CreatedAt, dashboardIds = u.Dashboards.Select(d => d.DashboardId).ToList(),
    };

    private static Guid? AgentId(HttpContext http) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;

    private static async Task<IResult> List(ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var users = await db.ClientUsers.AsNoTracking().Include(u => u.Dashboards).OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ToListAsync(ct);
        return Results.Ok(users.Select(ToResponse));
    }

    /// <summary>Only client dashboards can be assigned to client users.</summary>
    private static async Task<string?> CheckDashboardsAsync(TenantDbContext db, List<Guid>? ids, CancellationToken ct)
    {
        if (ids is not { Count: > 0 }) return null;
        var ok = await db.Dashboards.AsNoTracking().CountAsync(d => ids.Contains(d.Id) && d.IsClientDashboard && d.ScopeClientId != null, ct);
        return ok == ids.Distinct().Count() ? null : "Only client dashboards (with a client chosen) can be assigned to client users.";
    }

    private static async Task<IResult> Invite(InviteClientUserRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc, IEmailService email,
        IConfiguration config, HttpContext http, ILoggerFactory lf, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        var address = (req.Email ?? "").Trim().ToLowerInvariant();
        if (!address.Contains('@') || address.Length > 320) return Results.BadRequest(new { error = "Enter a valid email address." });
        if (string.IsNullOrWhiteSpace(req.FirstName)) return Results.BadRequest(new { error = "First name is required." });

        await using var db = dbf.Create();
        if (await db.ClientUsers.AnyAsync(u => u.Email == address, ct))
            return Results.Conflict(new { error = "A client user with that email already exists." });
        if (await CheckDashboardsAsync(db, req.DashboardIds, ct) is { } bad) return Results.BadRequest(new { error = bad });

        var user = ClientUser.Create(address, req.FirstName, req.LastName ?? "", req.CanPlayRecordings, AgentId(http));
        user.SetDashboards(req.DashboardIds ?? []);
        var token = user.IssueInvite();
        db.ClientUsers.Add(user);
        db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.Invited,
            $"recordings {(req.CanPlayRecordings ? "allowed" : "not allowed")}", ClientPortalAuthEndpoints.Ip(http), AgentId(http)));
        await db.SaveChangesAsync(ct);

        var sent = await SendAsync(user, token, tenant, email, config, reset: false, lf, ct);
        return Results.Ok(new { user = ToResponse(user), emailSent = sent });
    }

    private static async Task<bool> SendAsync(ClientUser user, string token, Tenant tenant, IEmailService email, IConfiguration config,
        bool reset, ILoggerFactory lf, CancellationToken ct)
    {
        var baseUrl = config["App:BaseUrl"] ?? "https://contactconnection.io";
        var host = $"https://{tenant.Subdomain}.{new Uri(baseUrl).Host}";
        var name = tenant.DisplayName ?? tenant.Name;
        try
        {
            await email.SendAsync(user.Email, ClientUserInviteEmail.Subject(name, reset),
                ClientUserInviteEmail.HtmlBody(name, user.FirstName, $"{host}/client/invite/{token}", $"{host}/client/login", reset), ct);
            return true;
        }
        catch (Exception ex)
        {
            lf.CreateLogger("AdminClientUsers").LogError(ex, "Failed to send the client-portal link to client user {ClientUserId}", user.Id);
            return false;
        }
    }

    private static async Task<IResult> Update(Guid id, UpdateClientUserRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc,
        ClientUserStatusReader status, HttpContext http, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var user = await db.ClientUsers.Include(u => u.Dashboards).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return Results.NotFound();
        if (await CheckDashboardsAsync(db, req.DashboardIds, ct) is { } bad) return Results.BadRequest(new { error = bad });

        var changes = new List<string>();
        if (user.IsActive != req.IsActive) changes.Add(req.IsActive ? "reactivated" : "deactivated");
        if (user.CanPlayRecordings != req.CanPlayRecordings) changes.Add($"recordings {(req.CanPlayRecordings ? "allowed" : "not allowed")}");
        var before = user.Dashboards.Select(d => d.DashboardId).ToHashSet();
        if (req.DashboardIds is { } ids && !before.SetEquals(ids)) changes.Add($"dashboards {before.Count} → {ids.Distinct().Count()}");

        user.Update(req.FirstName ?? user.FirstName, req.LastName ?? user.LastName, req.IsActive, req.CanPlayRecordings);
        if (req.DashboardIds is not null) user.SetDashboards(req.DashboardIds);
        if (changes.Count > 0)
            db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.Updated, string.Join(", ", changes),
                ClientPortalAuthEndpoints.Ip(http), AgentId(http)));
        await db.SaveChangesAsync(ct);
        status.Invalidate(user.Id);
        return Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> SendLink(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, IEmailService email,
        IConfiguration config, HttpContext http, ILoggerFactory lf, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var user = await db.ClientUsers.Include(u => u.Dashboards).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return Results.NotFound();
        if (!user.IsActive) return Results.BadRequest(new { error = "Reactivate the account first." });
        var reset = user.HasPassword;
        var token = user.IssueInvite();
        db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.Invited, reset ? "password link sent" : "invite resent",
            ClientPortalAuthEndpoints.Ip(http), AgentId(http)));
        await db.SaveChangesAsync(ct);
        var sent = await SendAsync(user, token, tenant, email, config, reset, lf, ct);
        return Results.Ok(new { user = ToResponse(user), emailSent = sent });
    }

    private static async Task<IResult> ResetMfa(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, HttpContext http, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var user = await db.ClientUsers.Include(u => u.Dashboards).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return Results.NotFound();
        user.ResetMfa();
        db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.MfaReset, null, ClientPortalAuthEndpoints.Ip(http), AgentId(http)));
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> Delete(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, ClientUserStatusReader status,
        HttpContext http, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var user = await db.ClientUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return Results.NotFound();
        db.ClientUsers.Remove(user);
        // The audit trail outlives the account.
        db.ClientUserAudit.Add(ClientUserAuditEntry.Create(user.Id, ClientUserAuditAction.Updated, $"deleted ({user.Email})",
            ClientPortalAuthEndpoints.Ip(http), AgentId(http)));
        await db.SaveChangesAsync(ct);
        status.Invalidate(user.Id);
        return Results.NoContent();
    }

    private static async Task<IResult> Audit(Guid? clientUserId, int? limit, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var q = db.ClientUserAudit.AsNoTracking().AsQueryable();
        if (clientUserId is { } uid) q = q.Where(a => a.ClientUserId == uid);
        var rows = await q.OrderByDescending(a => a.At).Take(Math.Clamp(limit ?? 200, 1, 1000)).ToListAsync(ct);
        var userIds = rows.Where(r => r.ClientUserId != null).Select(r => r.ClientUserId!.Value).Distinct().ToList();
        var agentIds = rows.Where(r => r.ByAgentId != null).Select(r => r.ByAgentId!.Value).Distinct().ToList();
        var users = await db.ClientUsers.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var agents = await db.Agents.AsNoTracking().Where(a => agentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.FullName, ct);
        return Results.Ok(rows.Select(r => new
        {
            r.Id, r.Action, r.Detail, r.IpAddress, r.At, r.ClientUserId,
            clientUser = r.ClientUserId is { } u ? users.GetValueOrDefault(u) : null,
            byAgent = r.ByAgentId is { } a ? agents.GetValueOrDefault(a) : null,
        }));
    }
}

public record InviteClientUserRequest(string? Email, string? FirstName, string? LastName, bool CanPlayRecordings, List<Guid>? DashboardIds);
public record UpdateClientUserRequest(string? FirstName, string? LastName, bool IsActive, bool CanPlayRecordings, List<Guid>? DashboardIds);
