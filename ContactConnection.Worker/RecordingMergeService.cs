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
/// Post-call recording pipeline. Every poll pass, for each active tenant:
///
///   1. <b>Enqueue</b> — any call record that has a recording (<c>recording_started_at</c> set),
///      is still retained, has no merged output yet, and has settled (no updates for
///      <c>SettleSeconds</c>) gets a <see cref="RecordingMergeJob"/> row.
///   2. <b>Claim</b> — pending jobs whose backoff has elapsed flip to <c>processing</c>.
///   3. <b>Process</b> — each claimed job: locate the call-audio WAV on disk, gather the call's
///      completed screen captures, hand them to <see cref="IRecordingMerger"/> (ffmpeg), and on
///      success stamp <c>CallRecord.RecordingUrl</c> with the streaming endpoint path.
///   4. <b>Reap</b> — jobs stuck in <c>processing</c> past <c>StuckJobMinutes</c> (worker crash /
///      restart mid-merge) are failed back (which re-queues them until <c>MaxAttempts</c>).
///
/// Single-instance assumption (same as <see cref="SubscriptionProcessingService"/>): claim isn't
/// atomic across workers. Fine for the current one-worker deployment; a multi-worker setup would
/// need a <c>SELECT … FOR UPDATE SKIP LOCKED</c> claim.
/// </summary>
public sealed class RecordingMergeService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRecordingMerger _merger;
    private readonly ILogger<RecordingMergeService> _logger;

    private readonly bool _enabled;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _settle;
    private readonly TimeSpan _retryBackoff;
    private readonly TimeSpan _stuckAfter;
    private readonly int _batchSize;
    private readonly string _audioSourceDir;
    private readonly string _outputPrefix;

    public RecordingMergeService(
        IServiceScopeFactory scopeFactory,
        IRecordingMerger merger,
        IConfiguration config,
        ILogger<RecordingMergeService> logger)
    {
        _scopeFactory = scopeFactory;
        _merger       = merger;
        _logger       = logger;

        _enabled        = !bool.TryParse(config["Recording:Merge:Enabled"], out var en) || en;   // default true
        _interval       = TimeSpan.FromSeconds(ConfigInt(config, "Recording:Merge:PollSeconds", 60));
        _settle         = TimeSpan.FromSeconds(ConfigInt(config, "Recording:Merge:SettleSeconds", 120));
        _retryBackoff   = TimeSpan.FromSeconds(ConfigInt(config, "Recording:Merge:RetryBackoffSeconds", 300));
        _stuckAfter     = TimeSpan.FromMinutes(ConfigInt(config, "Recording:Merge:StuckJobMinutes", 30));
        _batchSize      = ConfigInt(config, "Recording:Merge:BatchSize", 10);
        _audioSourceDir = Path.GetFullPath(config["Recording:Merge:AudioSourceDir"] ?? "freeswitch/recordings");
        _outputPrefix   = config["Recording:Merge:OutputBlobPrefix"] ?? "recordings";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("RecordingMergeService disabled (Recording:Merge:Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "RecordingMergeService started — poll {Interval}s, settle {Settle}s, audio dir {Dir}.",
            _interval.TotalSeconds, _settle.TotalSeconds, _audioSourceDir);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in recording-merge cycle.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("RecordingMergeService stopped.");
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
                await EnqueueAsync(tenant, ct);
                await ReapStuckAsync(tenant, ct);
                var claimedIds = await ClaimAsync(tenant, ct);
                foreach (var jobId in claimedIds)
                {
                    if (ct.IsCancellationRequested) return;
                    await ProcessAsync(tenant, jobId, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Recording-merge failed for tenant {Subdomain}.", tenant.Subdomain);
            }
        }
    }

    // ── 1. enqueue ──────────────────────────────────────────────────────────
    private async Task EnqueueAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = CreateTenantScope(tenant, out var repo);

        var ids = await repo.FindCallRecordIdsNeedingMergeAsync(
            DateTimeOffset.UtcNow - _settle, _batchSize, ct);
        if (ids.Count == 0) return;

        foreach (var callRecordId in ids)
            await repo.AddAsync(RecordingMergeJob.Create(tenant.Id, callRecordId), ct);

        await repo.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Queued {Count} recording-merge job(s) for tenant {Subdomain}.", ids.Count, tenant.Subdomain);
    }

    // ── 2. claim ────────────────────────────────────────────────────────────
    private async Task<IReadOnlyList<Guid>> ClaimAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = CreateTenantScope(tenant, out var repo);

        var now  = DateTimeOffset.UtcNow;
        var jobs = await repo.GetClaimableAsync(now, _batchSize, ct);
        if (jobs.Count == 0) return [];

        foreach (var job in jobs) job.Claim(now);
        await repo.SaveChangesAsync(ct);
        return jobs.Select(j => j.Id).ToList();
    }

    // ── 3. process one claimed job ──────────────────────────────────────────
    private async Task ProcessAsync(Tenant tenant, Guid jobId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        SetTenant(scope, tenant);

        var jobRepo    = scope.ServiceProvider.GetRequiredService<IRecordingMergeJobRepository>();
        var callRepo   = scope.ServiceProvider.GetRequiredService<ICallRecordRepository>();
        var screenRepo = scope.ServiceProvider.GetRequiredService<IScreenRecordingRepository>();

        var job = await jobRepo.GetByIdAsync(jobId, ct);
        if (job is null || job.Status != RecordingMergeJobStatus.Processing) return;

        var record = await callRepo.GetByIdAsync(job.CallRecordId, ct);
        if (record is null)
        {
            job.Fail("call record not found", _retryBackoff);
            await jobRepo.SaveChangesAsync(ct);
            return;
        }

        if (!record.RecordingRetained || record.RecordingStartedAt is null)
        {
            job.Skip("recording not retained / no start timestamp");
            await jobRepo.SaveChangesAsync(ct);
            return;
        }

        var audioPath = Path.Combine(_audioSourceDir, $"{job.CallRecordId}.wav");
        if (!File.Exists(audioPath))
        {
            // Could still be flushing, or already purged. Fail (re-queues) until MaxAttempts,
            // after which it lands in 'failed' and is visible for investigation.
            job.Fail($"call-audio file not found: {audioPath}", _retryBackoff);
            await jobRepo.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Merge job {JobId} (call {CallId}): audio file missing at {Path}", jobId, job.CallRecordId, audioPath);
            return;
        }

        var screens = (await screenRepo.ListByCallRecordAsync(job.CallRecordId, ct))
            .Where(s => s.Status == ScreenRecordingStatus.Complete)
            .Select(s => new ScreenRecordingInput(
                s.Id, s.StorageKey, s.Container, s.ReceivedChunkIndices, s.StartedAtServer, s.DurationMs))
            .ToList();

        var request = new RecordingMergeRequest(
            job.CallRecordId, audioPath, record.RecordingStartedAt.Value, screens, _outputPrefix);

        RecordingMergeResult result;
        try
        {
            result = await _merger.MergeAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.Fail($"merge threw: {ex.Message}", _retryBackoff);
            await jobRepo.SaveChangesAsync(ct);
            _logger.LogError(ex, "Merge job {JobId} (call {CallId}) threw.", jobId, job.CallRecordId);
            return;
        }

        if (!result.Success)
        {
            job.Fail(result.Error ?? "merge failed", _retryBackoff);
            await jobRepo.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Merge job {JobId} (call {CallId}) failed (attempt {Attempt}/{Max}): {Error}",
                jobId, job.CallRecordId, job.Attempts, job.MaxAttempts, result.Error);
            return;
        }

        // Success — write the call record first (the durable side effect), then the job.
        record.SetRecording($"/api/v1/call-records/{job.CallRecordId}/recording");
        await callRepo.SaveChangesAsync(ct);

        job.Succeed(
            result.OutputBlobKey!, result.OutputFormat!, result.OutputDurationMs, result.HadVideo,
            result.ScreenRecordingId, result.ScreenRecordingCount, result.FfmpegCommand);
        await jobRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Merged recording for call {CallId} (tenant {Subdomain}) → {Key} ({Format}, video={Video}).",
            job.CallRecordId, tenant.Subdomain, result.OutputBlobKey, result.OutputFormat, result.HadVideo);
    }

    // ── 4. reap stuck jobs ──────────────────────────────────────────────────
    private async Task ReapStuckAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        SetTenant(scope, tenant);
        var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();

        var cutoff = DateTimeOffset.UtcNow - _stuckAfter;
        var stuck = await db.RecordingMergeJobs
            .Where(j => j.Status == RecordingMergeJobStatus.Processing && j.StartedAt != null && j.StartedAt < cutoff)
            .ToListAsync(ct);
        if (stuck.Count == 0) return;

        foreach (var job in stuck)
            job.Fail("stuck in processing past the stuck-job threshold (worker restart or crash)", _retryBackoff);

        await db.SaveChangesAsync(ct);
        _logger.LogWarning(
            "Reaped {Count} stuck recording-merge job(s) for tenant {Subdomain}.", stuck.Count, tenant.Subdomain);
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private IServiceScope CreateTenantScope(Tenant tenant, out IRecordingMergeJobRepository repo)
    {
        var scope = _scopeFactory.CreateScope();
        SetTenant(scope, tenant);
        repo = scope.ServiceProvider.GetRequiredService<IRecordingMergeJobRepository>();
        return scope;
    }

    private static void SetTenant(IServiceScope scope, Tenant tenant) =>
        scope.ServiceProvider.GetRequiredService<TenantContext>().Current = tenant;

    private static int ConfigInt(IConfiguration config, string key, int fallback) =>
        int.TryParse(config[key], out var v) && v > 0 ? v : fallback;
}
