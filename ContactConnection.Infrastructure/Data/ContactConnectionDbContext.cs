using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Data;

public class ContactConnectionDbContext : DbContext
{
    public ContactConnectionDbContext(DbContextOptions<ContactConnectionDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantInvite> TenantInvites => Set<TenantInvite>();
    public DbSet<TenantAdminInvite> TenantAdminInvites => Set<TenantAdminInvite>();
    public DbSet<DataType> DataTypes => Set<DataType>();
    public DbSet<PortalApiDefinition> PortalApiDefinitions => Set<PortalApiDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new TenantInviteConfiguration());
        modelBuilder.ApplyConfiguration(new TenantAdminInviteConfiguration());
        modelBuilder.ApplyConfiguration(new DataTypeConfiguration());
        modelBuilder.ApplyConfiguration(new PortalApiDefinitionConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}