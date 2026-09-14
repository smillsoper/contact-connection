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
/// Time-bounded retention sweep for the PCI <c>SensitiveData</c> blob (captured card/CVV/SSN
/// digits — see ARCHITECTURE.md §24 and <see cref="CallRecord.StoreSensitiveData"/>). Nothing in
/// the platform currently reads/exports that blob (no payment-gateway or export-worker consumer
/// exists yet — see the "Export worker" section of §24 for the future confirmed-export wipe
/// path), so today this is a pure safety net: anything older than the resolved TTL gets wiped on
/// a schedule regardless of whether anything ever consumed it. When a real export/consumer is
/// built, it should still wipe immediately on confirmed transmission (per §24) — this sweep only
/// catches what that path misses (capture abandoned mid-flow, consumer never ran, etc.).
///
/// Same shape as <see cref="RecordingRetentionService"/>: per-tenant, batched, oldest-stored-first,
/// single-instance assumption, and — like recording retention's per-campaign
/// <c>RecordingRetentionDays</c> — TTL resolves per <see cref="Campaign.SensitiveDataRetentionMinutes"/>
/// with a fallback to the platform default (<c>SensitiveData:Retention:TtlMinutes</c>) for
/// campaigns with no override or no resolvable campaign. A campaign that runs a daily/weekly
/// secure export (FTPS, PGP, encrypted zip, etc.) needs its captured data to survive until that
/// job runs, not just the platform's short safety-net default.
/// </summary>
public sealed class SensitiveDataRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SensitiveDataRetentionService> _logger;

    private readonly bool _enabled;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;
    private readonly int _defaultTtlMinutes;

    public SensitiveDataRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<SensitiveDataRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        _enabled           = !bool.TryParse(config["SensitiveData:Retention:Enabled"], out var en) || en;   // default true
        _interval          = TimeSpan.FromMinutes(ConfigInt(config, "SensitiveData:Retention:PollMinutes", 15));
        _batchSize         = ConfigInt(config, "SensitiveData:Retention:BatchSize", 50);
        _defaultTtlMinutes = ConfigInt(config, "SensitiveData:Retention:TtlMinutes", 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("SensitiveDataRetentionService disabled (SensitiveData:Retention:Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "SensitiveDataRetentionService started — poll {Interval}m, batch {Batch}, default TTL {Ttl}m.",
            _interval.TotalMinutes, _batchSize, _defaultTtlMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in sensitive-data-retention cycle.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("SensitiveDataRetentionService stopped.");
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
                await WipeTenantAsync(tenant, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Sensitive-data retention sweep failed for tenant {Subdomain}.", tenant.Subdomain);
            }
        }
    }

    private async Task WipeTenantAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Current = tenant;

        var callRepo = scope.ServiceProvider.GetRequiredService<ICallRecordRepository>();
        var db       = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();

        var ids = await callRepo.FindWithSensitiveDataOldestFirstAsync(_batchSize, ct);
        if (ids.Count == 0) return;

        // One lookup of the campaigns referenced by this batch, not one per record — same
        // pattern as RecordingRetentionService.PurgeTenantAsync.
        var idList = ids.ToList();
        var campaignIds = await db.CallRecords
            .Where(r => idList.Contains(r.Id))
            .Select(r => r.CampaignId)
            .Distinct()
            .ToListAsync(ct);
        var ttlMinutesByCampaign = await db.Campaigns
            .Where(c => campaignIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.SensitiveDataRetentionMinutes, ct);

        var now = DateTimeOffset.UtcNow;
        var wiped = 0;

        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) return;

            var record = await callRepo.GetByIdAsync(id, ct);
            if (record is null || record.SensitiveData is null) continue;

            var ttlMinutes = ttlMinutesByCampaign.GetValueOrDefault(record.CampaignId) ?? _defaultTtlMinutes;
            var anchor     = record.SensitiveDataStoredAt ?? record.CreatedAt;
            if (anchor.AddMinutes(ttlMinutes) > now) continue;   // not due yet — campaigns vary, keep scanning the batch

            record.WipeSensitiveData("retention_expired");
            await callRepo.SaveChangesAsync(ct);
            wiped++;

            _logger.LogInformation(
                "Wiped sensitive data for call {CallId} (tenant {Subdomain}) — stored {Anchor:o}, TTL {Ttl}m.",
                id, tenant.Subdomain, anchor, ttlMinutes);
        }

        if (wiped > 0)
            _logger.LogInformation(
                "Sensitive-data retention: wiped {Wiped}/{Scanned} record(s) for tenant {Subdomain}.",
                wiped, ids.Count, tenant.Subdomain);
    }

    private static int ConfigInt(IConfiguration config, string key, int fallback) =>
        int.TryParse(config[key], out var v) && v > 0 ? v : fallback;
}
