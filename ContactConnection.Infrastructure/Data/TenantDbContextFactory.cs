using ContactConnection.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ContactConnection.Infrastructure.Data;

public interface ITenantDbContextFactory
{
    TenantDbContext Create(string schemaName);
}

public class TenantDbContextFactory : ITenantDbContextFactory
{
    private readonly IConfiguration _configuration;

    public TenantDbContextFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public TenantDbContext Create(string schemaName)
    {
        var baseConnStr = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured.");

        // Setting search_path routes all unqualified table references to the tenant schema first,
        // then public. This is how EF Core's single unqualified model maps to per-tenant tables.
        var connStr = new NpgsqlConnectionStringBuilder(baseConnStr)
        {
            SearchPath = $"{schemaName},public"
        }.ToString();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connStr, opts => opts.MigrationsAssembly("ContactConnection.Infrastructure"))
            .Options;

        return new TenantDbContext(options);
    }
}

/// <summary>
/// Scoped factory that resolves the current tenant from TenantContext and
/// creates a TenantDbContext scoped to that tenant's schema.
/// </summary>
public class ScopedTenantDbContextFactory
{
    private readonly TenantContext _tenantContext;
    private readonly ITenantDbContextFactory _factory;

    public ScopedTenantDbContextFactory(TenantContext tenantContext, ITenantDbContextFactory factory)
    {
        _tenantContext = tenantContext;
        _factory = factory;
    }

    public TenantDbContext Create()
    {
        if (_tenantContext.Current is null)
            throw new InvalidOperationException(
                "No tenant resolved for this request. Ensure TenantResolutionMiddleware is registered.");

        var db = _factory.Create(_tenantContext.Current.SchemaName);
        db.VisibleSupportAgentId = _tenantContext.SupportAgentId;
        return db;
    }
}
