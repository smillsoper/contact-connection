using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Worker;

/// <summary>
/// Retention sweep for the generic tenant/client/campaign key-value store
/// (<see cref="ContactConnection.Domain.Entities.StoredValue"/>). Unlike
/// <see cref="SensitiveDataRetentionService"/>, TTL is baked into each row's ExpiresAt at write
/// time (the Store Value node's retention dropdown) rather than resolved per-campaign here, and
/// expired rows are hard-deleted rather than null-out-wiped — there's no audit/wipe-reason
/// requirement for a cache value the way there is for PCI data. Same overall shape: per-tenant,
/// batched, oldest-expired-first, single-instance assumption.
/// </summary>
public sealed class StoredValueRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StoredValueRetentionService> _logger;

    private readonly bool _enabled;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;

    public StoredValueRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<StoredValueRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        _enabled   = !bool.TryParse(config["StoredValues:Retention:Enabled"], out var en) || en; // default true
        _interval  = TimeSpan.FromMinutes(ConfigInt(config, "StoredValues:Retention:PollMinutes", 15));
        _batchSize = ConfigInt(config, "StoredValues:Retention:BatchSize", 100);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("StoredValueRetentionService disabled (StoredValues:Retention:Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "StoredValueRetentionService started — poll {Interval}m, batch {Batch}.",
            _interval.TotalMinutes, _batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in stored-value-retention cycle.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("StoredValueRetentionService stopped.");
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        List<Tenant> tenants;
        using (var platformScope = _scopeFactory.CreateScope())
        {
            var platformDb = platformScope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
            tenants = await platformDb.Tenants.Where(t => t.IsActive).ToListAsync(ct);
        }

        foreach (var tenant in tenants)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                await SweepTenantAsync(tenant, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Stored-value retention sweep failed for tenant {Subdomain}.", tenant.Subdomain);
            }
        }
    }

    private async Task SweepTenantAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Current = tenant;

        var repo = scope.ServiceProvider.GetRequiredService<IStoredValueRepository>();

        var ids = await repo.FindExpiredOldestFirstAsync(_batchSize, ct);
        if (ids.Count == 0) return;

        var deleted = 0;
        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) return;

            var value = await repo.GetByIdAsync(id, ct);
            if (value is null) continue;

            repo.Remove(value);
            await repo.SaveChangesAsync(ct);
            deleted++;
        }

        if (deleted > 0)
            _logger.LogInformation(
                "Stored-value retention: deleted {Deleted}/{Scanned} row(s) for tenant {Subdomain}.",
                deleted, ids.Count, tenant.Subdomain);
    }

    private static int ConfigInt(IConfiguration config, string key, int fallback) =>
        int.TryParse(config[key], out var v) && v > 0 ? v : fallback;
}
