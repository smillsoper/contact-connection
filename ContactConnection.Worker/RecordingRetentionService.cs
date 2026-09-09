using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
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
/// Retention purge for finished call recordings. Every poll pass, for each active tenant, pulls a
/// batch of the oldest still-retained recordings and, for each one past its campaign's
/// <see cref="Campaign.RecordingRetentionDays"/> (measured from call end), deletes the audio and
/// any screen captures — the merged output blob, the screen-chunk blobs, and the raw call-audio
/// <c>.wav</c> on disk — then stamps <see cref="CallRecord.MarkRecordingPurged"/>. The recording
/// event trail (JSONB audit) and the call record itself stay; only the media goes.
///
/// Retention is per-campaign, so the batch is ordered oldest-call-first and each record's window
/// is evaluated in memory (a campaign with a short window shouldn't wait behind older calls on a
/// long one). Records with no resolvable campaign fall back to
/// <c>Recording:Retention:DefaultDays</c>. Single-instance assumption, same as
/// <see cref="RecordingMergeService"/>.
/// </summary>
public sealed class RecordingRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBlobStorage _blob;
    private readonly ILogger<RecordingRetentionService> _logger;

    private readonly bool _enabled;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;
    private readonly int _defaultDays;
    private readonly string _audioSourceDir;
    private readonly string _outputPrefix;

    public RecordingRetentionService(
        IServiceScopeFactory scopeFactory,
        IBlobStorage blob,
        IConfiguration config,
        ILogger<RecordingRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _blob         = blob;
        _logger       = logger;

        _enabled        = !bool.TryParse(config["Recording:Retention:Enabled"], out var en) || en;   // default true
        _interval       = TimeSpan.FromHours(ConfigInt(config, "Recording:Retention:PollHours", 12));
        _batchSize      = ConfigInt(config, "Recording:Retention:BatchSize", 50);
        _defaultDays    = ConfigInt(config, "Recording:Retention:DefaultDays", 90);
        // Same artifacts the merge service produces / consumes — reuse its keys.
        _audioSourceDir = Path.GetFullPath(config["Recording:Merge:AudioSourceDir"] ?? "freeswitch/recordings");
        _outputPrefix   = (config["Recording:Merge:OutputBlobPrefix"] ?? "recordings").Trim('/');
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("RecordingRetentionService disabled (Recording:Retention:Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "RecordingRetentionService started — poll {Interval}h, batch {Batch}, default window {Days}d.",
            _interval.TotalHours, _batchSize, _defaultDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in recording-retention cycle.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("RecordingRetentionService stopped.");
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
                await PurgeTenantAsync(tenant, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Recording-retention purge failed for tenant {Subdomain}.", tenant.Subdomain);
            }
        }
    }

    private async Task PurgeTenantAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Current = tenant;

        var callRepo = scope.ServiceProvider.GetRequiredService<ICallRecordRepository>();
        var db       = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();

        var ids = await callRepo.FindRetainedRecordingIdsOldestFirstAsync(_batchSize, ct);
        if (ids.Count == 0) return;

        // One lookup of the campaigns referenced by this batch, not one per record.
        var idList = ids.ToList();
        var campaignIds = await db.CallRecords
            .Where(r => idList.Contains(r.Id))
            .Select(r => r.CampaignId)
            .Distinct()
            .ToListAsync(ct);
        var retentionByCampaign = await db.Campaigns
            .Where(c => campaignIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.RecordingRetentionDays, ct);

        var now = DateTimeOffset.UtcNow;
        var purged = 0;

        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) return;

            var record = await callRepo.GetByIdAsync(id, ct);
            if (record is null || !record.RecordingRetained) continue;

            var days   = retentionByCampaign.GetValueOrDefault(record.CampaignId, _defaultDays);
            var anchor = record.CallEndAt ?? record.RecordingStoppedAt ?? record.CreatedAt;
            if (anchor.AddDays(days) > now) continue;   // not due yet — campaigns vary, so keep scanning the batch

            await _blob.DeletePrefixAsync($"{_outputPrefix}/{id}", ct);   // merged output ({prefix}/{id}/merged.*)
            await _blob.DeletePrefixAsync($"screen/{id}", ct);            // screen-capture chunks
            TryDeleteFile(Path.Combine(_audioSourceDir, $"{id}.wav"));    // raw stereo call audio

            record.MarkRecordingPurged("retention_expired");
            await callRepo.SaveChangesAsync(ct);
            purged++;

            _logger.LogInformation(
                "Purged recording for call {CallId} (tenant {Subdomain}) — {Days}d window, call ended {Anchor:o}.",
                id, tenant.Subdomain, days, anchor);
        }

        if (purged > 0)
            _logger.LogInformation(
                "Recording retention: purged {Purged}/{Scanned} recording(s) for tenant {Subdomain}.",
                purged, ids.Count, tenant.Subdomain);
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recording retention: could not delete raw audio file {Path}.", path);
        }
    }

    private static int ConfigInt(IConfiguration config, string key, int fallback) =>
        int.TryParse(config[key], out var v) && v > 0 ? v : fallback;
}
