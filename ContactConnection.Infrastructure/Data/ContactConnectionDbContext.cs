using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data.Configurations;
using ContactConnection.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Data;

public class ContactConnectionDbContext : DbContext
{
    public ContactConnectionDbContext(DbContextOptions<ContactConnectionDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<SipGateway> SipGateways => Set<SipGateway>();
    public DbSet<TenantInvite> TenantInvites => Set<TenantInvite>();
    public DbSet<TenantAdminInvite> TenantAdminInvites => Set<TenantAdminInvite>();
    public DbSet<DataType> DataTypes => Set<DataType>();
    public DbSet<PortalApiDefinition> PortalApiDefinitions => Set<PortalApiDefinition>();
    public DbSet<PortalApiEndpoint> PortalApiEndpoints => Set<PortalApiEndpoint>();
    public DbSet<PhoneNumberRouting> PhoneNumberRoutings => Set<PhoneNumberRouting>();
    public DbSet<EntityVersion> EntityVersions => Set<EntityVersion>();
    public DbSet<CredentialAuditEntry> CredentialAuditEntries => Set<CredentialAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new SipGatewayConfiguration());
        modelBuilder.ApplyConfiguration(new TenantInviteConfiguration());
        modelBuilder.ApplyConfiguration(new TenantAdminInviteConfiguration());
        modelBuilder.ApplyConfiguration(new DataTypeConfiguration());
        modelBuilder.ApplyConfiguration(new PortalApiDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new PortalApiEndpointConfiguration());
        modelBuilder.ApplyConfiguration(new PhoneNumberRoutingConfiguration());
        modelBuilder.ApplyConfiguration(new EntityVersionConfiguration());
        modelBuilder.ApplyConfiguration(new CredentialAuditEntryConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}