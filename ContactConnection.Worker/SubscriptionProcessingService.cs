using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Worker;

/// <summary>
/// Background service that runs hourly and processes due AutoShip subscriptions.
///
/// For each active tenant, it queries subscriptions whose NextShipDate has passed,
/// creates a renewal Order + OrderLine, confirms inventory, and advances the schedule.
///
/// Uses IServiceScopeFactory to create a per-tenant DI scope, pre-populating
/// TenantContext so that all scoped services (repositories, inventory) work correctly
/// without modification.
/// </summary>
public class SubscriptionProcessingService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionProcessingService> _logger;

    public SubscriptionProcessingService(
        IServiceScopeFactory scopeFactory,
        ILogger<SubscriptionProcessingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SubscriptionProcessingService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Processing due subscriptions at {Time:u}", DateTimeOffset.UtcNow);

            try
            {
                await ProcessAllTenantsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in subscription processing cycle.");
            }

            JobHeartbeats.Report("Subscriptions", Interval);
            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessAllTenantsAsync(CancellationToken ct)
    {
        // Load active tenants from the platform schema
        using var platformScope = _scopeFactory.CreateScope();
        var platformDb = platformScope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
        var tenants    = await platformDb.Tenants.Where(t => t.IsActive).ToListAsync(ct);

        _logger.LogInformation("Found {Count} active tenant(s) to process.", tenants.Count);

        foreach (var tenant in tenants)
        {
            try
            {
                await ProcessTenantAsync(tenant, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing subscriptions for tenant {TenantId} ({Subdomain}).",
                    tenant.Id, tenant.Subdomain);
            }
        }
    }

    private async Task ProcessTenantAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        // Pre-populate TenantContext so all scoped services (repositories, inventory) resolve
        // the correct schema without needing HTTP request middleware.
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Current = tenant;

        var subscriptionRepo = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var orderCreator     = scope.ServiceProvider.GetRequiredService<ISubscriptionOrderCreator>();

        var due = await subscriptionRepo.GetDueAsync(DateTimeOffset.UtcNow, ct);

        if (due.Count == 0)
            return;

        _logger.LogInformation(
            "Processing {Count} due subscription(s) for tenant {Subdomain}.", due.Count, tenant.Subdomain);

        foreach (var subscription in due)
        {
            try
            {
                var order = await orderCreator.CreateRenewalOrderAsync(subscription, ct);
                subscription.RecordShipment();
                await subscriptionRepo.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Subscription {SubscriptionId} (SKU: {Sku}) renewed — Order {OrderId} created.",
                    subscription.Id, subscription.Sku, order.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to process subscription {SubscriptionId} (SKU: {Sku}) for tenant {Subdomain}.",
                    subscription.Id, subscription.Sku, tenant.Subdomain);
            }
        }
    }
}
