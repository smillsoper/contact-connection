using System.Security.Cryptography;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContactConnection.Worker;

/// <summary>
/// The Export Worker's engine (S180). Every few seconds, for each active tenant: reap runs stuck in <c>running</c>
/// (a worker restart mid-file), claim queued runs with <c>FOR UPDATE SKIP LOCKED</c> — safe with more than one Worker —
/// and generate them, at most <c>Exports:MaxConcurrent</c> (5) at once across all tenants. Each file is rendered to a temp
/// file (so it can be hashed and sized), then stored at <c>exports/{definitionId}/{runId}/{fileName}</c> for download and,
/// in session 2, delivery. A template error fails the run at once (retrying won't fix it); anything else retries with a
/// backoff up to the run's MaxAttempts.
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
    private readonly SemaphoreSlim _slots;
    private readonly int _maxConcurrent;
    private readonly List<Task> _inFlight = [];
    private readonly HashSet<Guid> _unmigrated = [];

    public ExportRunService(IServiceScopeFactory scopeFactory, IBlobStorage blobs, IConfiguration config, ILogger<ExportRunService> logger)
    {
        _scopeFactory = scopeFactory;
        _blobs = blobs;
        _logger = logger;
        _enabled = !bool.TryParse(config["Exports:Enabled"], out var en) || en;
        _interval = TimeSpan.FromSeconds(int.TryParse(config["Exports:PollSeconds"], out var p) && p > 0 ? p : 5);
        _stuckAfter = TimeSpan.FromMinutes(int.TryParse(config["Exports:StuckRunMinutes"], out var s) && s > 0 ? s : 30);
        _maxConcurrent = int.TryParse(config["Exports:MaxConcurrent"], out var m) && m > 0 ? m : 5;
        _slots = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) { _logger.LogInformation("ExportRunService disabled (Exports:Enabled=false)."); return; }
        _logger.LogInformation("ExportRunService started — poll {Interval}s, max {Max} at once.", _interval.TotalSeconds, _maxConcurrent);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CycleAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Export cycle failed."); }
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

        foreach (var tenant in tenants)
        {
            if (ct.IsCancellationRequested || _slots.CurrentCount == 0) return;
            try
            {
                await ReapStuckAsync(tenant, ct);
                foreach (var runId in await ClaimAsync(tenant, _slots.CurrentCount, ct))
                {
                    await _slots.WaitAsync(ct);
                    _inFlight.Add(Task.Run(async () =>
                    {
                        try { await ProcessAsync(tenant, runId, ct); }
                        finally { _slots.Release(); }
                    }, CancellationToken.None));
                }
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                // A tenant schema that hasn't had the exports migration yet — say so once, not every few seconds.
                if (_unmigrated.Add(tenant.Id))
                    _logger.LogWarning("Tenant {Subdomain} has no export tables yet (run the tenant migrations); skipping it.", tenant.Subdomain);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Export queue failed for tenant {Subdomain}.", tenant.Subdomain);
            }
        }
    }

    /// <summary>Atomically flips up to <paramref name="limit"/> due runs to running. SKIP LOCKED: another Worker claiming
    /// at the same moment gets different rows, never the same one.</summary>
    private async Task<List<Guid>> ClaimAsync(Tenant tenant, int limit, CancellationToken ct)
    {
        if (limit <= 0) return [];
        using var scope = TenantScope(tenant);
        await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE export_runs SET status = 'running', attempts = attempts + 1, started_at = @now
            WHERE id IN (SELECT id FROM export_runs
                         WHERE status = 'queued' AND next_attempt_at <= @now
                         ORDER BY queued_at LIMIT @limit FOR UPDATE SKIP LOCKED)
            RETURNING id
            """, conn);
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("limit", limit);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return ids;
    }

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
            var request = new ExportGenerationRequest(run.Spec, run.DefinitionName, run.Id, run.IsTest, run.DataSource,
                run.WindowStart, run.WindowEnd);
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
            try { File.Delete(temp); } catch { /* temp dir cleanup is best-effort */ }
        }
    }

    private async Task ReapStuckAsync(Tenant tenant, CancellationToken ct)
    {
        using var scope = TenantScope(tenant);
        await using var db = scope.ServiceProvider.GetRequiredService<ScopedTenantDbContextFactory>().Create();
        var cutoff = DateTimeOffset.UtcNow - _stuckAfter;
        var stuck = await db.ExportRuns
            .Where(r => r.Status == ExportRunStatus.Running && r.StartedAt != null && r.StartedAt < cutoff).ToListAsync(ct);
        if (stuck.Count == 0) return;
        foreach (var run in stuck) run.Fail("stopped mid-file (worker restart) — retrying", TimeSpan.Zero);
        await db.SaveChangesAsync(ct);
        _logger.LogWarning("Re-queued {Count} stuck export run(s) for tenant {Subdomain}.", stuck.Count, tenant.Subdomain);
    }

    private IServiceScope TenantScope(Tenant tenant)
    {
        var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Current = tenant;
        return scope;
    }
}
