using System.Security.Cryptography;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Domain.ValueObjects.Exports;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Exports;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContactConnection.Worker;

/// <summary>
/// The Export Worker's engine (S180). Every few seconds, for each active tenant:
///
///   1. <b>Schedule</b> (every 30 s) — each live export with a schedule gets a run queued for every run time that has come
///      due since the last one (at most the last 7 if the Worker was down; a unique index means two Workers can't queue
///      the same run time twice). Its window comes from the schedule in the export's own time zone.
///   2. <b>Reap</b> runs / deliveries stuck in <c>running</c> (a Worker restart mid-file).
///   3. <b>Generate</b> claimed runs (<c>FOR UPDATE SKIP LOCKED</c>), rendered to a temp file, hashed, stored at
///      <c>exports/{definitionId}/{runId}/{fileName}</c>. A run marked Deliver then queues one delivery per enabled target.
///   4. <b>Deliver</b> claimed deliveries — the stored file to SFTP / FTPS / email (encrypted per target), retried with a
///      growing backoff.
///   5. <b>Retention</b> (hourly) — stored files older than <c>Exports:RetentionDays</c> (90) are deleted; the file a vendor
///      approved never is.
///
/// Generation and delivery share <c>Exports:MaxConcurrent</c> (5) slots across all tenants.
/// </summary>
public sealed class ExportRunService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBlobStorage _blobs;
    private readonly ILogger<ExportRunService> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _stuckAfter;
    private readonly TimeSpan _retryBackoff = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _scheduleEvery = TimeSpan.FromSeconds(30);
    private readonly int _retentionDays;
    private readonly SemaphoreSlim _slots;
    private readonly int _maxConcurrent;
    private readonly List<Task> _inFlight = [];
    private readonly HashSet<Guid> _unmigrated = [];
    private DateTimeOffset _lastScheduling = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRetention = DateTimeOffset.MinValue;

    private const int MaxCatchUp = 7;

    public ExportRunService(IServiceScopeFactory scopeFactory, IBlobStorage blobs, IConfiguration config, ILogger<ExportRunService> logger)
    {
        _scopeFactory = scopeFactory;
        _blobs = blobs;
        _logger = logger;
        _enabled = !bool.TryParse(config["Exports:Enabled"], out var en) || en;
        _interval = TimeSpan.FromSeconds(int.TryParse(config["Exports:PollSeconds"], out var p) && p > 0 ? p : 5);
        _stuckAfter = TimeSpan.FromMinutes(int.TryParse(config["Exports:StuckRunMinutes"], out var s) && s > 0 ? s : 30);
        _retentionDays = int.TryParse(config["Exports:RetentionDays"], out var r) && r > 0 ? r : 90;
        _maxConcurrent = int.TryParse(config["Exports:MaxConcurrent"], out var m) && m > 0 ? m : 5;
        _slots = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) { _logger.LogInformation("ExportRunService disabled (Exports:Enabled=false)."); return; }
        _logger.LogInformation("ExportRunService started — poll {Interval}s, max {Max} at once, files kept {Days} days.",
            _interval.TotalSeconds, _maxConcurrent, _retentionDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CycleAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Export cycle failed."); }
            JobHeartbeats.Report("Data exports", _interval);
            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        // Let in-flight files finish (or observe cancellation) before the host stops.
        try { await Task.WhenAll(_inFlight); } catch { /* logged per run */ }
    }

    private async Task CycleAsync(CancellationToken ct)
    {
        _inFlight.RemoveAll(t => t.IsCompleted);

        List<Tenant> tenants;
        using (var scope = _scopeFactory.CreateScope())
            tenants = await scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>()
                .Tenants.Where(t => t.IsActive).ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var schedule = now - _lastScheduling >= _scheduleEvery;
        var retention = now - _lastRetention >= TimeSpan.FromHours(1);
        if (schedule) _lastScheduling = now;
        if (retention) _lastRetention = now;

        foreach (var tenant in tenants)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                if (schedule) await ScheduleAsync(tenant, ct);
                if (retention) await RetentionAsync(tenant, ct);
                await ReapStuckAsync(tenant, ct);
                foreach (var runId in await ClaimAsync(tenant, "export_runs", "queued_at", _slots.CurrentCount, ct))
                    Start(() => ProcessAsync(tenant, runId, ct));
                foreach (var deliveryId in await ClaimAsync(tenant, "export_deliveries", "queued_at", _slots.CurrentCount, ct))
                    Start(() => DeliverAsync(tenant, deliveryId, ct));
            }
            catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
            {
                // A tenant schema behind on the exports migrations — say so once, not every few seconds.
                if (_unmigrated.Add(tenant.Id))
                    _logger.LogWarning("Tenant {Subdomain} is missing export tables/columns (run the tenant migrations); skipping it.", tenant.Subdomain);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Export queue failed for tenant {Subdomain}.", tenant.Subdomain);
            }
        }
    }

    private void Start(Func<Task> work)
    {
        // Claims never exceed the free slots, so this wait is immediate.
        _slots.Wait();
        _inFlight.Add(Task.Run(async () =>
        {
            try { await work(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Anything thrown before a job's own error handling would otherwise vanish with the task.
                _logger.LogError(ex, "Export job crashed; it will be retried after the stuck-job timeout.");
            }
            finally { _slots.Release(); }
        }, CancellationToken.None));
    }

    /// <summary>Atomically flips up to <paramref name="limit"/> due rows of a queue table to running. SKIP LOCKED: another
    /// Worker claiming at the same moment gets different rows, never the same one.</summary>
    private async Task<List<Guid>> ClaimAsync(Tenant tenant, string table, string orderBy, int limit, CancellationToken ct)
    {
        if (limit <= 0) return [];
        using var scope = TenantScope(tenant);
        await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        // table / orderBy are compile-time constants from this class, never input.
        await using var cmd = new NpgsqlCommand($"""
            UPDATE {table} SET status = 'running', attempts = attempts + 1, started_at = @now
            WHERE id IN (SELECT id FROM {table}
                         WHERE status = 'queued' AND next_attempt_at <= @now
                         ORDER BY {orderBy} LIMIT @limit FOR UPDATE SKIP LOCKED)
            RETURNING id
            """, conn);
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("limit", limit);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    // ── 1. Schedule ────────────────────────────────────────────────────────────

    private async Task ScheduleAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = TenantScope(tenant);
        await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();
        var defs = await db.ExportDefinitions.Where(d => d.Status == ExportStatus.Live && d.Schedule != null).ToListAsync(ct);
        if (defs.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        var queued = 0;
        foreach (var def in defs)
        {
            var schedule = def.Schedule!;
            var due = ExportScheduleCalculator.Occurrences(schedule, def.LastScheduledFor ?? now, now).ToList();
            if (due.Count == 0) continue;
            if (due.Count > MaxCatchUp)
            {
                _logger.LogWarning("Export {Name}: {Missed} scheduled runs were missed; queuing only the last {Max}.",
                    def.Name, due.Count, MaxCatchUp);
                due = due.TakeLast(MaxCatchUp).ToList();
            }

            // "Since the last run" starts where the last real file's window ended.
            DateTimeOffset? lastEnd = await db.ExportRuns
                .Where(r => r.DefinitionId == def.Id && !r.IsTest && r.Status != ExportRunStatus.Failed)
                .MaxAsync(r => (DateTimeOffset?)r.WindowEnd, ct);
            var already = (await db.ExportRuns.Where(r => r.DefinitionId == def.Id && r.ScheduledFor != null && due.Contains(r.ScheduledFor.Value))
                .Select(r => r.ScheduledFor!.Value).ToListAsync(ct)).ToHashSet();

            foreach (var runAt in due)
            {
                if (already.Contains(runAt)) continue;
                var (start, end) = ExportScheduleCalculator.Window(schedule, def.Spec.TimeZone, runAt, lastEnd);
                if (end <= start) continue;
                db.ExportRuns.Add(ExportRun.Queue(def, ExportRunKind.Scheduled, false, ExportDataSource.Production, start, end,
                    null, "Schedule", deliver: schedule.AutoDeliver, scheduledFor: runAt));
                lastEnd = end;
                queued++;
            }
            def.MarkScheduled(due[^1]);
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another Worker queued the same run time first — it'll be generated once, by them.
            _logger.LogInformation("Scheduled export runs for {Subdomain} were already queued elsewhere.", tenant.Subdomain);
            return;
        }
        if (queued > 0) _logger.LogInformation("Queued {Count} scheduled export run(s) for {Subdomain}.", queued, tenant.Subdomain);
    }

    // ── 3. Generate ────────────────────────────────────────────────────────────

    private async Task ProcessAsync(Tenant tenant, Guid runId, CancellationToken ct)
    {
        using var scope = TenantScope(tenant);
        await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();
        var generator = scope.ServiceProvider.GetRequiredService<IExportGenerator>();
        var run = await db.ExportRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Status != ExportRunStatus.Running) return;

        var temp = Path.Combine(Path.GetTempPath(), $"cc-export-{run.Id:N}.tmp");
        try
        {
            var request = Request(run);
            // A real card-data file (S182) never touches disk in the clear: built in memory, stored encrypted.
            if (run.HoldsCardData)
            {
                await ProcessCardFileAsync(db, run, generator, request, scope.ServiceProvider, ct);
                return;
            }
            ExportGenerationResult result;
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                result = await generator.GenerateAsync(request, file, ct);

            if (!result.Success)
            {
                run.Fail(result.Error ?? "export failed", _retryBackoff, permanent: true);
                await db.SaveChangesAsync(CancellationToken.None);
                _logger.LogWarning("Export run {RunId} ({Name}) failed: {Error}", run.Id, run.DefinitionName, result.Error);
                return;
            }

            var fileName = generator.RenderFileName(request);
            var key = $"exports/{run.DefinitionId}/{run.Id}/{fileName}";
            string sha;
            long size;
            await using (var file = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                size = file.Length;
                sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
                file.Position = 0;
                await _blobs.PutAsync(key, file, generator.ContentType(run.Spec), ct);
            }

            run.Succeed(result.RowCount, result.CallCount, fileName, key, generator.ContentType(run.Spec), size, sha);

            // Scheduled / "Run now and send" files go straight to every enabled target.
            if (run.Deliver && await db.ExportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == run.DefinitionId, ct) is { } def)
                foreach (var target in def.DeliveryTargets.Where(t => t.Enabled))
                    db.ExportDeliveries.Add(ExportDelivery.Queue(run, target.Id, target.Name, target.Type, run.RequestedByName));

            await db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Export run {RunId} ({Name}{Test}) → {File}: {Rows} rows from {Calls} calls.",
                run.Id, run.DefinitionName, run.IsTest ? ", test" : "", fileName, result.RowCount, result.CallCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Export run {RunId} threw.", run.Id);
            run.Fail(ex.Message, _retryBackoff);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>Real card-data files (preview and test files are masked) show the actual card values.</summary>
    private static ExportGenerationRequest Request(ExportRun run) =>
        new(run.Spec, run.DefinitionName, run.Id, run.IsTest, run.DataSource, run.WindowStart, run.WindowEnd, Kind: run.Kind,
            MaskCardData: !run.HoldsCardData);

    /// <summary>
    /// A real card-data file (S182): generated in memory, encrypted with the platform's data key (AES-256-GCM) before it's
    /// stored — the only copy kept. Delivery decrypts it in memory and PGP-encrypts it to the recipient.
    /// </summary>
    private async Task ProcessCardFileAsync(TenantDbContext db, ExportRun run, IExportGenerator generator, ExportGenerationRequest request,
        IServiceProvider services, CancellationToken ct)
    {
        var protector = services.GetRequiredService<ISensitiveDataProtector>();
        if (services.GetRequiredService<TenantContext>().Current?.FeatureFlags.CardDataExports != true)
        {
            run.Fail("Card-data exports are switched off for this account.", _retryBackoff, permanent: true);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }
        if (!protector.IsConfigured)
        {
            run.Fail("Card-data files need the platform's data key (SensitiveData:MasterKey), which isn't configured.", _retryBackoff, permanent: true);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }
        using var memory = new MemoryStream();
        var result = await generator.GenerateAsync(request, memory, ct);
        if (!result.Success)
        {
            run.Fail(result.Error ?? "export failed", _retryBackoff, permanent: true);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }
        var bytes = memory.ToArray();
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var sealedText = Encoding.UTF8.GetBytes(protector.Protect(Convert.ToBase64String(bytes)));
        Array.Clear(bytes);
        var fileName = generator.RenderFileName(request);
        var key = $"exports/{run.DefinitionId}/{run.Id}/{fileName}.sealed";
        using (var sealedStream = new MemoryStream(sealedText))
            await _blobs.PutAsync(key, sealedStream, "application/octet-stream", ct);

        run.Succeed(result.RowCount, result.CallCount, fileName, key, generator.ContentType(run.Spec), memory.Length, sha);
        run.SetCardCalls(result.CardCallIds ?? []);
        db.ExportAuditEntries.Add(ExportAuditEntry.Record(run.TenantId, run.DefinitionId, run.Id, ExportAuditAction.CardDataGenerated,
            run.RequestedByName, $"{fileName}: card data for {run.CardCallIds.Count} call(s), stored encrypted"));
        if (run.Deliver && await db.ExportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == run.DefinitionId, ct) is { } def)
            foreach (var target in def.DeliveryTargets.Where(t => t.Enabled))
                db.ExportDeliveries.Add(ExportDelivery.Queue(run, target.Id, target.Name, target.Type, run.RequestedByName));
        await db.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("Card-data export run {RunId} ({Name}) → {File}: {Rows} rows, card data for {Cards} calls (stored encrypted).",
            run.Id, run.DefinitionName, fileName, result.RowCount, run.CardCallIds.Count);
    }

    /// <summary>The card-data file, PGP-encrypted to the target in memory; only the encrypted file is written for upload.</summary>
    private async Task<string?> SealedToPgpAsync(ExportRun run, ExportDeliveryTarget target, string temp, IServiceProvider services, CancellationToken ct)
    {
        await using var blob = await _blobs.OpenReadAsync(run.BlobKey!, ct);
        if (blob is null) return null;
        using var reader = new StreamReader(blob, Encoding.UTF8);
        var plain = Convert.FromBase64String(services.GetRequiredService<ISensitiveDataProtector>().Unprotect(await reader.ReadToEndAsync(ct)));
        try
        {
            var pgp = new PgpCore.PGP(new PgpCore.EncryptionKeys(target.PgpPublicKey!));
            using var input = new MemoryStream(plain);
            await using var output = File.Create(temp);
            await pgp.EncryptAsync(input, output, armor: false, withIntegrityCheck: true, name: run.FileName);
        }
        finally { Array.Clear(plain); }
        return temp;
    }

    /// <summary>
    /// Once every enabled target has the card-data file (S182), the calls' card data has done its job: wiped, and the
    /// wipe audited. A target still failing keeps it — the aged-card-data alert shows that.
    /// </summary>
    private static async Task WipeCardDataIfDeliveredAsync(TenantDbContext db, ExportRun run, ExportDefinition def, CancellationToken ct)
    {
        if (!run.HoldsCardData || run.CardDataWipedAt is not null || run.CardCallIds.Count == 0) return;
        var targets = def.DeliveryTargets.Where(t => t.Enabled).Select(t => t.Id).ToList();
        var delivered = await db.ExportDeliveries.AsNoTracking()
            .Where(d => d.RunId == run.Id && d.Status == ExportDeliveryStatus.Succeeded).Select(d => d.TargetId).ToListAsync(ct);
        if (targets.Count == 0 || targets.Except(delivered).Any()) return;

        var ids = run.CardCallIds;
        var records = await db.CallRecords.Where(r => ids.Contains(r.Id) && r.SensitiveData != null).ToListAsync(ct);
        foreach (var r in records) r.WipeSensitiveData("exported");
        var tracked = await db.ExportRuns.FirstAsync(x => x.Id == run.Id, ct);
        tracked.MarkCardDataWiped();
        db.ExportAuditEntries.Add(ExportAuditEntry.Record(run.TenantId, run.DefinitionId, run.Id, ExportAuditAction.CardDataWiped, null,
            $"{run.FileName}: delivered to every target — card data wiped for {records.Count} call(s)"));
        await db.SaveChangesAsync(CancellationToken.None);
    }

    // ── 4. Deliver ─────────────────────────────────────────────────────────────

    private async Task DeliverAsync(Tenant tenant, Guid deliveryId, CancellationToken ct)
    {
        using var scope = TenantScope(tenant);
        await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();
        var delivery = await db.ExportDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery is null || delivery.Status != ExportDeliveryStatus.Running) return;

        var temp = Path.Combine(Path.GetTempPath(), $"cc-export-send-{delivery.Id:N}.tmp");
        try
        {
            var run = await db.ExportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == delivery.RunId, ct);
            var def = await db.ExportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == delivery.DefinitionId, ct);
            var target = def?.DeliveryTargets.FirstOrDefault(t => t.Id == delivery.TargetId);
            if (run?.BlobKey is null || run.FileDeletedAt is not null) { await FailAsync(db, delivery, "The file is no longer stored.", true); return; }
            if (target is null) { await FailAsync(db, delivery, $"The delivery target '{delivery.TargetName}' was removed.", true); return; }
            if (!target.Enabled) { await FailAsync(db, delivery, $"The delivery target '{target.Name}' is turned off.", true); return; }

            var sendTarget = target;
            var sendName = run.FileName!;
            if (run.HoldsCardData)
            {
                // S182: FTPS + PGP only, checked again at send time whatever the saved settings say.
                if (target.CardDataProblem() is { } problem) { await FailAsync(db, delivery, problem, true); return; }
                if (await SealedToPgpAsync(run, target, temp, scope.ServiceProvider, ct) is null)
                { await FailAsync(db, delivery, "The file is no longer stored.", true); return; }
                sendTarget = target with { Encryption = ExportEncryption.None };   // already PGP-encrypted, in memory
                sendName = run.FileName + ".pgp";
            }
            else
            {
                await using var blob = await _blobs.OpenReadAsync(run.BlobKey, ct);
                if (blob is null) { await FailAsync(db, delivery, "The file is no longer stored.", true); return; }
                await using var file = File.Create(temp);
                await blob.CopyToAsync(file, ct);
            }

            var sender = scope.ServiceProvider.GetRequiredService<IExportDeliveryService>();
            var result = await sender.DeliverAsync(new ExportDeliveryRequest(
                tenant.Subdomain, sendTarget, temp, sendName, run.ContentType ?? "application/octet-stream",
                ExportGenerator.Context(Request(run)), run.Spec.TimeZone), ct);
            var sentAs = result.SentAs;

            // First send to an unpinned SFTP server: pin the key it presented, so every later send must match it.
            if (result.PinnedHostKey is { } hostKey
                && await db.ExportDefinitions.FirstOrDefaultAsync(d => d.Id == delivery.DefinitionId, ct) is { } tracked)
            {
                tracked.SetDeliveryTargets(tracked.DeliveryTargets
                    .Select(t => t.Id == target.Id && string.IsNullOrWhiteSpace(t.HostKeyFingerprint) ? t with { HostKeyFingerprint = hostKey } : t)
                    .ToList());
                db.ExportAuditEntries.Add(ExportAuditEntry.Record(delivery.TenantId, delivery.DefinitionId, delivery.RunId,
                    ExportAuditAction.HostKeyPinned, null, $"{target.Name}: pinned host key {hostKey} on first connection"));
            }

            delivery.Succeed(sentAs);
            db.ExportAuditEntries.Add(ExportAuditEntry.Record(delivery.TenantId, delivery.DefinitionId, delivery.RunId,
                ExportAuditAction.Delivered, delivery.RequestedByName, $"{run.FileName} → {target.Name} ({sentAs})"));
            await db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Export file {File} delivered to {Target}.", run.FileName, target.Name);
            if (def is not null) await WipeCardDataIfDeliveredAsync(db, run, def, ct);
        }
        catch (ExportDeliveryException ex)
        {
            await FailAsync(db, delivery, ex.Message, ex.Permanent);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Export delivery {DeliveryId} threw.", delivery.Id);
            await FailAsync(db, delivery, ex.Message, false);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private async Task FailAsync(TenantDbContext db, ExportDelivery delivery, string error, bool permanent)
    {
        delivery.Fail(error, permanent);
        if (delivery.Status == ExportDeliveryStatus.Failed)
            db.ExportAuditEntries.Add(ExportAuditEntry.Record(delivery.TenantId, delivery.DefinitionId, delivery.RunId,
                ExportAuditAction.DeliveryFailed, null,
                error.StartsWith(delivery.TargetName + ":", StringComparison.Ordinal) ? error : $"{delivery.TargetName}: {error}"));
        await db.SaveChangesAsync(CancellationToken.None);
        _logger.LogWarning("Export delivery to {Target} {Outcome}: {Error}", delivery.TargetName,
            delivery.Status == ExportDeliveryStatus.Failed ? "failed" : "will retry", error);
    }

    // ── 2. Reap, 5. Retention ──────────────────────────────────────────────────

    private async Task ReapStuckAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = TenantScope(tenant);
        await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();
        var cutoff = DateTimeOffset.UtcNow - _stuckAfter;
        var stuck = await db.ExportRuns
            .Where(r => r.Status == ExportRunStatus.Running && r.StartedAt != null && r.StartedAt < cutoff).ToListAsync(ct);
        foreach (var run in stuck) run.Fail("stopped mid-file (worker restart) — retrying", TimeSpan.Zero);
        var stuckSends = await db.ExportDeliveries
            .Where(d => d.Status == ExportDeliveryStatus.Running && d.StartedAt != null && d.StartedAt < cutoff).ToListAsync(ct);
        foreach (var d in stuckSends) d.Fail("stopped mid-send (worker restart) — retrying");
        if (stuck.Count + stuckSends.Count == 0) return;
        await db.SaveChangesAsync(ct);
        _logger.LogWarning("Re-queued {Runs} stuck export run(s) and {Sends} delivery(ies) for {Subdomain}.",
            stuck.Count, stuckSends.Count, tenant.Subdomain);
    }

    private async Task RetentionAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = TenantScope(tenant);
        await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);
        var keep = await db.ExportDefinitions.Where(d => d.ApprovedRunId != null).Select(d => d.ApprovedRunId!.Value).ToListAsync(ct);
        var expired = await db.ExportRuns
            .Where(r => r.BlobKey != null && r.FileDeletedAt == null && r.FinishedAt != null && r.FinishedAt < cutoff && !keep.Contains(r.Id))
            .OrderBy(r => r.FinishedAt).Take(200).ToListAsync(ct);
        foreach (var run in expired)
        {
            await _blobs.DeleteAsync(run.BlobKey!, ct);
            run.MarkFileDeleted();
            db.ExportAuditEntries.Add(ExportAuditEntry.Record(run.TenantId, run.DefinitionId, run.Id,
                ExportAuditAction.FileExpired, null, $"{run.FileName} deleted after {_retentionDays} days"));
        }
        if (expired.Count == 0) return;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted {Count} export file(s) past {Days} days for {Subdomain}.", expired.Count, _retentionDays, tenant.Subdomain);
    }

    private IServiceScope TenantScope(Tenant tenant)
    {
        var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Current = tenant;
        return scope;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* temp cleanup is best-effort */ }
    }
}
