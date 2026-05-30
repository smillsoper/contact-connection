using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Data;

/// <summary>
/// DbContext for all per-tenant tables (call_records, call_interactions, etc.).
/// Does NOT set a default schema — table names are unqualified.
/// The correct tenant schema is applied via PostgreSQL search_path on the connection,
/// set by TenantDbContextFactory for each request. See ARCHITECTURE.md §6.
/// </summary>
public class TenantDbContext : DbContext
{
    public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options) { }

    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<CallRecord> CallRecords => Set<CallRecord>();
    public DbSet<CallInteraction> CallInteractions => Set<CallInteraction>();
    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<FlowSession> FlowSessions => Set<FlowSession>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductKit> ProductKits => Set<ProductKit>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<TenantApiDefinition> TenantApiDefinitions => Set<TenantApiDefinition>();
    public DbSet<TenantApiEndpoint> TenantApiEndpoints => Set<TenantApiEndpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new CallRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CallInteractionConfiguration());
        modelBuilder.ApplyConfiguration(new FlowConfiguration());
        modelBuilder.ApplyConfiguration(new FlowSessionConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new ProductKitConfiguration());
        modelBuilder.ApplyConfiguration(new OfferConfiguration());
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new ProductCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ProductAttributeConfiguration());
        modelBuilder.ApplyConfiguration(new ProductAttributeValueConfiguration());
        modelBuilder.ApplyConfiguration(new CustomFieldDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new CustomFieldValueConfiguration());
        modelBuilder.ApplyConfiguration(new TenantApiDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new TenantApiEndpointConfiguration());
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // Auto-update updated_at on any modified CallRecord
        foreach (var entry in ChangeTracker.Entries<CallRecord>())
        {
            if (entry.State == EntityState.Modified)
                entry.Property("UpdatedAt").CurrentValue = DateTimeOffset.UtcNow;
        }
        return base.SaveChangesAsync(ct);
    }
}
