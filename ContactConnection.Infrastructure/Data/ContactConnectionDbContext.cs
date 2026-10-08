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
    public DbSet<BroadcastStation> BroadcastStations => Set<BroadcastStation>();
    public DbSet<ZipCodeLocation> ZipCodes => Set<ZipCodeLocation>();
    public DbSet<AreaCodeLocation> AreaCodes => Set<AreaCodeLocation>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<SupportSession> SupportSessions => Set<SupportSession>();
    public DbSet<PlatformHealthCheck> HealthChecks => Set<PlatformHealthCheck>();
    public DbSet<HealthSample> HealthSamples => Set<HealthSample>();
    public DbSet<HealthIncident> HealthIncidents => Set<HealthIncident>();
    public DbSet<HealthSettings> HealthSettings => Set<HealthSettings>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<PortOrder> PortOrders => Set<PortOrder>();

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
        modelBuilder.ApplyConfiguration(new BroadcastStationConfiguration());
        modelBuilder.ApplyConfiguration(new ZipCodeLocationConfiguration());
        modelBuilder.ApplyConfiguration(new AreaCodeLocationConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceLineConfiguration());
        modelBuilder.ApplyConfiguration(new SupportSessionConfiguration());
        modelBuilder.ApplyConfiguration(new PlatformHealthCheckConfiguration());
        modelBuilder.ApplyConfiguration(new HealthSampleConfiguration());
        modelBuilder.ApplyConfiguration(new HealthIncidentConfiguration());
        modelBuilder.ApplyConfiguration(new HealthSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new PlatformUserConfiguration());
        modelBuilder.ApplyConfiguration(new PortOrderConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}