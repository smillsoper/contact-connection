using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Commerce;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Media;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Worker;

/// <summary>
/// Runs the retroactive batch jobs (S171): "recalculate past calls" from Admin → Commissions and
/// "replay media attribution" from Admin → Media Agencies. Checks every tenant for
/// pending batches every few seconds and runs them one at a time, oldest first. A batch left "running"
/// by a restart is picked up again — recalculation is idempotent, so a re-run only rewrites what still
/// differs.
/// </summary>
public sealed class CommissionRecalcService(IServiceScopeFactory scopeFactory, ILogger<CommissionRecalcService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Commission recalculation cycle failed"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunPendingAsync(CancellationToken ct)
    {
        List<Tenant> tenants;
        using (var platformScope = scopeFactory.CreateScope())
        {
            var platformDb = platformScope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
            tenants = await platformDb.Tenants.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
        }

        foreach (var tenant in tenants)
        {
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().Current = tenant;
            await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();

            CommissionRecalcBatch? batch;
            try
            {
                batch = await db.CommissionRecalcBatches
                    .Where(b => b.Status == CommissionRecalcStatus.Pending || b.Status == CommissionRecalcStatus.Running)
                    .OrderBy(b => b.CreatedAt).FirstOrDefaultAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A tenant whose schema hasn't had the commissions migration yet — skip it.
                logger.LogDebug(ex, "Skipping commission recalculation for tenant {Subdomain}", tenant.Subdomain);
                continue;
            }
            if (batch is null)
            {
                try { await RunMediaReplayAsync(scope.ServiceProvider, db, tenant, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Skipping media replay for tenant {Subdomain}", tenant.Subdomain);
                }
                continue;
            }

            logger.LogInformation("Commission recalculation {BatchId} starting for tenant {Subdomain}", batch.Id, tenant.Subdomain);
            try
            {
                await CommissionRecalculator.RunAsync(db, batch, ct);
                logger.LogInformation("Commission recalculation {BatchId} done: {Changed}/{Total} calls changed, difference {Difference}",
                    batch.Id, batch.ChangedCalls, batch.TotalCalls, batch.Difference);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Commission recalculation {BatchId} failed", batch.Id);
                db.ChangeTracker.Clear();
                var failed = await db.CommissionRecalcBatches.FirstAsync(b => b.Id == batch.Id, ct);
                failed.Fail(ex.Message);
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task RunMediaReplayAsync(IServiceProvider services, TenantDbContext db, Tenant tenant, CancellationToken ct)
    {
        var batch = await db.MediaReplayBatches
            .Where(b => b.Status == CommissionRecalcStatus.Pending || b.Status == CommissionRecalcStatus.Running)
            .OrderBy(b => b.CreatedAt).FirstOrDefaultAsync(ct);
        if (batch is null) return;

        logger.LogInformation("Media replay {BatchId} starting for tenant {Subdomain}", batch.Id, tenant.Subdomain);
        try
        {
            var platformDb = services.GetRequiredService<ContactConnectionDbContext>();
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(tenant.Timezone ?? "UTC"); }
            catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.Utc; }
            await MediaReplayer.RunAsync(db, platformDb, batch, tz, ct);
            logger.LogInformation("Media replay {BatchId} done: {Changed}/{Total} calls changed", batch.Id, batch.ChangedCalls, batch.TotalCalls);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Media replay {BatchId} failed", batch.Id);
            db.ChangeTracker.Clear();
            var failed = await db.MediaReplayBatches.FirstAsync(b => b.Id == batch.Id, ct);
            failed.Fail(ex.Message);
            await db.SaveChangesAsync(ct);
        }
    }
}
