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
/// Retention is per-campaign — and on "Always record, retain by disposition" campaigns per CALL (S181,
/// <see cref="RecordingRetentionPolicy"/>: each interaction's disposition / category rule; keep if any keeps, for the
/// longest period; missing / unmapped dispositions use the campaign's own period; discard and "conversation only" wait
/// <c>Recording:Retention:DispositionGraceHours</c> so a wrong disposition can be fixed first). So every retained
/// recording is walked a page at a time, oldest first. Records with no resolvable campaign fall back to
/// <c>Recording:Retention:DefaultDays</c>. Single-instance assumption, same as <see cref="RecordingMergeService"/>.
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
    private readonly TimeSpan _dispositionGrace;
    private readonly ContactConnection.Infrastructure.Telephony.Recording.RecordingTrimmer _trimmer;

    public RecordingRetentionService(
        IServiceScopeFactory scopeFactory,
        IBlobStorage blob,
        IConfiguration config,
        ILogger<RecordingRetentionService> logger,
        ContactConnection.Infrastructure.Telephony.Recording.RecordingTrimmer trimmer)
    {
        _trimmer = trimmer;
        // "Retain by disposition" waits this long after the call before discarding or trimming, so a disposition can be
        // corrected first (Call Records edit, AI summary confirm).
        _dispositionGrace = TimeSpan.FromHours(ConfigInt(config, "Recording:Retention:DispositionGraceHours", 24));
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
        await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();

        var campaigns = await db.Campaigns.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => (c.RecordingMode, c.RecordingRetentionDays, c.UnmappedRecordingRetentionDays), ct);
        var categories = await db.DispositionCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var dispositions = await db.Dispositions.AsNoTracking().ToDictionaryAsync(d => d.Id, ct);

        var now = DateTimeOffset.UtcNow;
        var purged = 0;
        var trimmed = 0;
        var scanned = 0;
        // Every retained recording (S181): retention now varies per call, so a newer call due for discard must not wait
        // behind older ones kept for a year. Ids first, then a page of records at a time.
        var allIds = await db.CallRecords.AsNoTracking()
            .Where(r => r.RecordingRetained && r.RecordingStartedAt != null)
            .OrderBy(r => r.CallEndAt ?? r.CreatedAt).Select(r => r.Id).ToListAsync(ct);

        foreach (var chunk in allIds.Chunk(_batchSize))
        {
            if (ct.IsCancellationRequested) return;
            var page = await db.CallRecords.Include(r => r.Interactions).Where(r => chunk.Contains(r.Id)).AsSplitQuery().ToListAsync(ct);
            foreach (var record in page)
            {
                if (ct.IsCancellationRequested) return;
                scanned++;
                var anchor = record.CallEndAt ?? record.RecordingStoppedAt ?? record.CreatedAt;
                var campaign = campaigns.TryGetValue(record.CampaignId, out var c) ? c : (RecordingMode: "", RecordingRetentionDays: _defaultDays, UnmappedRecordingRetentionDays: (int?)null);

                if (campaign.RecordingMode != RecordingMode.RecordAlwaysRetainByDisposition)
                {
                    if (anchor.AddDays(campaign.RecordingRetentionDays) <= now)
                    {
                        await PurgeAsync(record, "retention_expired", ct);
                        purged++;
                    }
                    continue;
                }

                // ── Retain by disposition (S181) ──
                var rules = record.Interactions.Select(i =>
                {
                    var d = i.DispositionId is { } id ? dispositions.GetValueOrDefault(id) : null;
                    return RecordingRetentionPolicy.ForDisposition(d, d is null ? null : categories.GetValueOrDefault(d.CategoryId));
                }).ToList();
                var decision = RecordingRetentionPolicy.Decide(campaign.RecordingRetentionDays, campaign.UnmappedRecordingRetentionDays, rules);
                var settled = anchor + _dispositionGrace <= now;   // time to correct a mistaken disposition first

                if (decision.Action == RecordingKeep.Discard)
                {
                    if (!settled) continue;
                    await PurgeAsync(record, "discarded_by_disposition", ct);
                    purged++;
                    continue;
                }

                if (anchor.AddDays(decision.Days) <= now)
                {
                    await PurgeAsync(record, "retention_expired", ct);
                    purged++;
                    continue;
                }

                if (decision.Action == RecordingKeep.Conversation && record.RecordingTrimmedAt is null && settled)
                {
                    switch (await TrimToConversationAsync(db, record, ct))
                    {
                        case TrimOutcome.Trimmed: trimmed++; break;
                        case TrimOutcome.NoConversation:
                            await PurgeAsync(record, "discarded_no_conversation", ct);
                            purged++;
                            break;
                    }
                }
            }
        }

        if (purged + trimmed > 0)
            _logger.LogInformation(
                "Recording retention for {Subdomain}: purged {Purged}, trimmed to conversation {Trimmed} (of {Scanned} retained).",
                tenant.Subdomain, purged, trimmed, scanned);

        async Task PurgeAsync(CallRecord record, string reason, CancellationToken token)
        {
            await _blob.DeletePrefixAsync($"{_outputPrefix}/{record.Id}", token);   // merged output ({prefix}/{id}/merged.*)
            await _blob.DeletePrefixAsync($"screen/{record.Id}", token);            // screen-capture chunks
            TryDeleteFile(Path.Combine(_audioSourceDir, $"{record.Id}.wav"));       // raw stereo call audio
            record.MarkRecordingPurged(reason);
            await db.SaveChangesAsync(token);
            _logger.LogInformation("Purged recording for call {CallId} (tenant {Subdomain}) — {Reason}.", record.Id, tenant.Subdomain, reason);
        }
    }

    private enum TrimOutcome { Trimmed, NoConversation, NotYet, Failed }

    /// <summary>Cuts the recording to start where the caller reached an agent (the call's first "active" state). Waits for
    /// the merge job to finish so the playback file exists to cut; a call that never reached an agent has no conversation.</summary>
    private async Task<TrimOutcome> TrimToConversationAsync(TenantDbContext db, CallRecord record, CancellationToken ct)
    {
        var job = await db.RecordingMergeJobs.AsNoTracking().FirstOrDefaultAsync(j => j.CallRecordId == record.Id, ct);
        if (job is { Status: RecordingMergeJobStatus.Pending or RecordingMergeJobStatus.Processing }) return TrimOutcome.NotYet;

        var connectedAt = await db.CallStateHistory.AsNoTracking()
            .Where(s => s.CallRecordId == record.Id && s.State == "active")
            .OrderBy(s => s.Sequence).Select(s => (DateTimeOffset?)s.EnteredAt).FirstOrDefaultAsync(ct);
        if (connectedAt is null) return TrimOutcome.NoConversation;

        var offset = (connectedAt.Value - record.RecordingStartedAt!.Value).TotalSeconds;
        var error = await _trimmer.TrimAsync(Path.Combine(_audioSourceDir, $"{record.Id}.wav"),
            job?.Status == RecordingMergeJobStatus.Complete ? job.OutputBlobKey : null, offset, ct);
        if (error is not null)
        {
            _logger.LogWarning("Trim to conversation failed for call {CallId}: {Error} — will retry next pass.", record.Id, error);
            return TrimOutcome.Failed;
        }
        record.MarkRecordingTrimmed((int)Math.Round(Math.Max(0, offset)));
        await db.SaveChangesAsync(ct);
        return TrimOutcome.Trimmed;
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
