using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static ContactConnection.Domain.Entities.Permission;
using static ContactConnection.Domain.Entities.LandingPage;

namespace ContactConnection.Infrastructure.Tenants;

public class TenantProvisioningService : ITenantProvisioningService
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantInviteRepository _invites;
    private readonly ContactConnectionDbContext _db;
    private readonly ITenantDbContextFactory _tenantDbContextFactory;

    public TenantProvisioningService(
        ITenantRepository tenants,
        ITenantInviteRepository invites,
        ContactConnectionDbContext db,
        ITenantDbContextFactory tenantDbContextFactory)
    {
        _tenants = tenants;
        _invites = invites;
        _db = db;
        _tenantDbContextFactory = tenantDbContextFactory;
    }

    public async Task<(Tenant Tenant, string? InviteToken)> ProvisionAsync(
        string name,
        string subdomain,
        string timezone,
        TenantFeatureFlags? featureFlags = null,
        string? inviteEmail = null,
        CancellationToken ct = default)
    {
        if (await _tenants.SubdomainExistsAsync(subdomain, ct))
            throw new InvalidOperationException($"Subdomain '{subdomain}' is already taken.");

        var tenant = Tenant.Create(name, subdomain, timezone, featureFlags, inviteEmail);

        await _tenants.AddAsync(tenant, ct);

        // 1. Create the PostgreSQL schema
        // SchemaName is system-generated ("tenant_" + normalized subdomain) — not user input.
        var schemaDdl = $"CREATE SCHEMA IF NOT EXISTS \"{tenant.SchemaName}\"";
#pragma warning disable EF1002
        await _db.Database.ExecuteSqlRawAsync(schemaDdl, ct);
#pragma warning restore EF1002

        // 2. Apply all tenant-scoped EF migrations to the new schema
        await using var tenantCtx = _tenantDbContextFactory.Create(tenant.SchemaName);
        await tenantCtx.Database.MigrateAsync(ct);

        // 3. Seed built-in roles
        await SeedBuiltInRolesAsync(tenantCtx, tenant.Id, ct);

        // 4. Create invite record if an email was provided
        string? inviteToken = null;
        if (!string.IsNullOrWhiteSpace(inviteEmail))
        {
            var invite = TenantInvite.Create(tenant.Id, inviteEmail);
            inviteToken = invite.Token;
            await _invites.AddAsync(invite, ct);
        }

        // 5. Save tenant + invite records atomically
        await _tenants.SaveChangesAsync(ct);

        return (tenant, inviteToken);
    }

    private static async Task SeedBuiltInRolesAsync(TenantDbContext ctx, Guid tenantId, CancellationToken ct)
    {
        var hasRoles = await ctx.Roles.AnyAsync(ct);
        if (hasRoles) return;

        ctx.Roles.AddRange(
            Role.Create(tenantId, "Administrator", Permission.All.ToList(), AdminDashboard, isBuiltIn: true),
            Role.Create(tenantId, "Supervisor",    [AgentsView, FlowsView, CallsView, CallsExport, SupervisorMonitor, SupervisorOverride, ReportsView], AgentPortal, isBuiltIn: true),
            Role.Create(tenantId, "Agent",         [CallsView], AgentPortal, isBuiltIn: true)
        );

        await ctx.SaveChangesAsync(ct);
    }

    public async Task<MigrationResult> MigrateAllTenantsAsync(CancellationToken ct = default)
    {
        var tenants = await _tenants.GetAllAsync(ct);
        var errors = new List<string>();
        var migrated = 0;

        foreach (var tenant in tenants)
        {
            try
            {
                await using var tenantCtx = _tenantDbContextFactory.Create(tenant.SchemaName);
                await tenantCtx.Database.MigrateAsync(ct);
                await SeedBuiltInRolesAsync(tenantCtx, tenant.Id, ct);
                migrated++;
            }
            catch (Exception ex)
            {
                errors.Add($"{tenant.Subdomain}: {ex.Message}");
            }
        }

        return new MigrationResult(migrated, errors);
    }
}
