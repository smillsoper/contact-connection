using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Services;

public interface ITenantProvisioningService
{
    /// <summary>
    /// Creates the tenant record and provisions its dedicated PostgreSQL schema.
    /// Returns the tenant and, if an invite email was provided, the invite token.
    /// </summary>
    Task<(Tenant Tenant, string? InviteToken)> ProvisionAsync(
        string name,
        string subdomain,
        string timezone,
        TenantFeatureFlags? featureFlags = null,
        string? inviteEmail = null,
        CancellationToken ct = default);

    /// <summary>
    /// Applies any pending EF migrations to every tenant schema.
    /// Returns a summary of tenants migrated and any that errored.
    /// </summary>
    Task<MigrationResult> MigrateAllTenantsAsync(CancellationToken ct = default);
}

public record MigrationResult(int Migrated, List<string> Errors);
