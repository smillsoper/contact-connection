using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Services;

/// <summary>
/// Scoped service holding the resolved tenant for the current request.
/// Populated by TenantResolutionMiddleware. Consumed by TenantDbContextFactory
/// and any application service that needs tenant-aware behavior.
/// </summary>
public class TenantContext
{
    public Tenant? Current { get; set; }
    public bool HasTenant => Current is not null;
    /// <summary>Set when the request comes from a ContactConnection support session (S184) — that support account is
    /// visible to its own requests only.</summary>
    public Guid? SupportAgentId { get; set; }
}
