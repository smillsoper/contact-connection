using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.FreeSwitchEsl;
using ContactConnection.Infrastructure.Health;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;

namespace ContactConnection.Worker;

/// <summary>
/// Platform health (S184): once a minute, measures the core services, telephony, the Worker's own jobs and the background
/// work queues, and hands the results to <see cref="HealthRecorder"/> (state, history, incidents, alert emails). Also
/// reports the Worker's heartbeat for the API's watchdog, and reads the API's heartbeat + facts (its FreeSWITCH event
/// connection, agents' softphone connections) that only the API knows.
/// </summary>
public sealed class PlatformHealthService(IServiceScopeFactory scopes, IConnectionMultiplexer redis, IConfiguration config,
    ILogger<PlatformHealthService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);   // let the jobs start first
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await redis.GetDatabase().StringSetAsync(HealthKeys.WorkerHeartbeat, DateTimeOffset.UtcNow.ToString("o"), TimeSpan.FromHours(1));
                var results = await MeasureAsync(stoppingToken);
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<HealthRecorder>().RecordAsync(results, stoppingToken);
                if (DateTimeOffset.UtcNow - _lastPrune > TimeSpan.FromHours(1)) await PruneAsync(scope, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Health check round failed."); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task<List<HealthResult>> MeasureAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var results = new List<HealthResult>
        {
            new("worker", 0, $"Running since {JobHeartbeats.StartedAt:yyyy-MM-dd HH:mm} UTC"),
        };

        // ── API heartbeat + what only the API knows ──
        ApiFacts? api = null;
        try
        {
            var raw = await redis.GetDatabase().StringGetAsync(HealthKeys.ApiFacts);
            if (!raw.IsNullOrEmpty) api = JsonSerializer.Deserialize<ApiFacts>(raw.ToString());
        }
        catch (Exception ex) { logger.LogDebug(ex, "Health: API facts unreadable"); }
        if (api is null) results.Add(new("api", null, "The API hasn't reported in.", HealthStatus.Critical));
        else
        {
            var age = (now - api.At).TotalSeconds;
            results.Add(new("api", Math.Round(age), age < 90 ? "Reporting normally" : $"Last report {Math.Round(age)} s ago"));
            var stale = age >= 90;
            results.Add(stale ? new("esl", null, "Unknown — the API isn't reporting.", HealthStatus.Unknown)
                : api.EslConnectedSince is { } since ? new("esl", null, $"Connected since {since:HH:mm} UTC", HealthStatus.Ok)
                : api.StartedAt is { } apiStart && now - apiStart < TimeSpan.FromMinutes(1)
                    ? new("esl", null, "The API just started — connecting to FreeSWITCH", HealthStatus.Unknown)
                : new("esl", null, "Disconnected — inbound calls can't be handled.", HealthStatus.Critical));
            results.Add(stale || api.AgentsOnCall < 3
                ? new("agent_connections", api.AgentsOnCall == 0 ? null : Math.Round(100.0 * api.AgentsPoor / api.AgentsOnCall),
                    stale ? "Unknown — the API isn't reporting." : $"{api.AgentsPoor} of {api.AgentsOnCall} on calls poor (judged at 3+)", HealthStatus.Unknown)
                : new("agent_connections", Math.Round(100.0 * api.AgentsPoor / api.AgentsOnCall), $"{api.AgentsPoor} of {api.AgentsOnCall} agents on calls"));
        }

        // ── Data stores ──
        results.AddRange(await MeasurePostgresAsync(ct));
        results.Add(await MeasureRedisAsync());
        results.Add(MeasureStorage());

        // ── Telephony ──
        results.AddRange(await MeasureFreeSwitchAsync(ct));
        results.Add(await MeasureTurnAsync(ct));

        // ── Jobs ──
        foreach (var (job, interval, last) in JobHeartbeats.All())
        {
            var (warn, crit) = JobHeartbeats.Limits(interval);
            var since = now - (last ?? JobHeartbeats.StartedAt);
            var status = since >= crit ? HealthStatus.Critical : since >= warn ? HealthStatus.Warning : HealthStatus.Ok;
            results.Add(new($"job:{job}", Math.Round(since.TotalMinutes, 1),
                last is null ? "Hasn't run yet" : $"Runs every {Every(interval)}; last {last:HH:mm} UTC", status));
        }

        // ── Calls and queued work, across tenants ──
        results.AddRange(await MeasureTenantsAsync(now, ct));
        return results;
    }

    private static string Every(TimeSpan t) => t.TotalHours >= 1 ? $"{t.TotalHours:0.#} h" : t.TotalMinutes >= 1 ? $"{t.TotalMinutes:0.#} min" : $"{t.TotalSeconds:0} s";

    private async Task<List<HealthResult>> MeasurePostgresAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(config.GetConnectionString("DefaultConnection"));
            var sw = Stopwatch.StartNew();
            await conn.OpenAsync(ct);
            await using (var ping = new NpgsqlCommand("SELECT 1", conn)) await ping.ExecuteScalarAsync(ct);
            var ms = sw.Elapsed.TotalMilliseconds;
            await using var cmd = new NpgsqlCommand("SELECT (SELECT count(*) FROM pg_stat_activity), current_setting('max_connections')::int", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            var used = r.GetInt64(0); var max = r.GetInt32(1);
            return
            [
                new("postgres", Math.Round(ms), "Answering"),
                new("postgres_connections", Math.Round(100.0 * used / max), $"{used} of {max} connections"),
            ];
        }
        catch (Exception ex)
        {
            return
            [
                new("postgres", null, $"Not answering: {ex.Message}", HealthStatus.Critical),
                new("postgres_connections", null, "Unknown", HealthStatus.Unknown),
            ];
        }
    }

    private async Task<HealthResult> MeasureRedisAsync()
    {
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return new("redis", Math.Round(latency.TotalMilliseconds, 1), "Answering");
        }
        catch (Exception ex) { return new("redis", null, $"Not answering: {ex.Message}", HealthStatus.Critical); }
    }

    private HealthResult MeasureStorage()
    {
        try
        {
            var root = Path.GetFullPath(config["Storage:LocalRoot"] ?? "storage");
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            var pct = 100.0 * drive.AvailableFreeSpace / drive.TotalSize;
            return new("storage", Math.Round(pct, 1), $"{drive.AvailableFreeSpace / 1_073_741_824.0:0.#} GB free of {drive.TotalSize / 1_073_741_824.0:0} GB");
        }
        catch (Exception ex) { return new("storage", null, $"Couldn't read: {ex.Message}", HealthStatus.Unknown); }
    }

    private async Task<List<HealthResult>> MeasureFreeSwitchAsync(CancellationToken ct)
    {
        var gateway = config["Health:TrunkGateway"] ?? "signalwire";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await using var esl = new FreeSwitchEslClient(config["FreeSWITCH:Host"] ?? "127.0.0.1",
                int.TryParse(config["FreeSWITCH:EslPort"], out var p) ? p : 8021, config["FreeSWITCH:EslPassword"] ?? "ClueCon", logger);
            var sw = Stopwatch.StartNew();
            await esl.ConnectAsync(timeout.Token);
            await esl.ApiAsync("status", timeout.Token);
            var ms = sw.Elapsed.TotalMilliseconds;
            var gw = await esl.ApiAsync($"sofia status gateway {gateway}", timeout.Token);
            var state = gw.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("State"))?.Split('\t', ' ').LastOrDefault(s => s.Length > 0);
            var channels = await esl.ApiAsync("show channels count", timeout.Token);
            var count = int.TryParse(new string(channels.Trim().TakeWhile(char.IsDigit).ToArray()), out var c) ? c : (int?)null;
            if (count is null && channels.Contains("total.")) count = int.TryParse(channels.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out c) ? c : null;
            return
            [
                new("freeswitch", Math.Round(ms), "Answering"),
                gw.Contains("Invalid Gateway", StringComparison.OrdinalIgnoreCase)
                    ? new("trunk", null, $"No gateway named '{gateway}'", HealthStatus.Critical)
                    : state == "REGED" ? new("trunk", null, "Registered", HealthStatus.Ok)
                    : new("trunk", null, $"Not registered (state {state ?? "unknown"})", HealthStatus.Critical),
                new("live_calls", count, count is null ? "Couldn't count" : $"{count} channel(s) up"),
            ];
        }
        catch (Exception ex)
        {
            return
            [
                new("freeswitch", null, $"Not answering: {ex.Message}", HealthStatus.Critical),
                new("trunk", null, "Unknown — FreeSWITCH isn't answering", HealthStatus.Unknown),
                new("live_calls", null, "Unknown", HealthStatus.Unknown),
            ];
        }
    }

    /// <summary>A STUN binding request to the TURN server (coturn answers STUN on the same port).</summary>
    private async Task<HealthResult> MeasureTurnAsync(CancellationToken ct)
    {
        var host = config["Health:TurnHost"] ?? "127.0.0.1";
        var port = int.TryParse(config["Health:TurnPort"], out var tp) ? tp : 3478;
        try
        {
            using var udp = new UdpClient();
            udp.Connect(host, port);
            var req = new byte[20];
            req[1] = 0x01;                                                   // Binding request
            req[4] = 0x21; req[5] = 0x12; req[6] = 0xA4; req[7] = 0x42;      // magic cookie
            RandomNumberGenerator.Fill(req.AsSpan(8, 12));                   // transaction id
            var sw = Stopwatch.StartNew();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await udp.SendAsync(req, ct);
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    var res = await udp.ReceiveAsync(wait.Token);
                    if (res.Buffer.Length >= 20 && res.Buffer[0] == 0x01 && res.Buffer[1] == 0x01 && res.Buffer.AsSpan(8, 12).SequenceEqual(req.AsSpan(8, 12)))
                        return new("turn", Math.Round(sw.Elapsed.TotalMilliseconds), $"Answering at {host}:{port}");
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            }
            return new("turn", null, $"No answer from {host}:{port} — remote softphones may have no audio.", HealthStatus.Critical);
        }
        catch (Exception ex) { return new("turn", null, $"Couldn't reach {host}:{port}: {ex.Message}", HealthStatus.Critical); }
    }

    private async Task<List<HealthResult>> MeasureTenantsAsync(DateTimeOffset now, CancellationToken ct)
    {
        int queued = 0, abandoned = 0, exportsFailed = 0, callbacksLate = 0, mergesWaiting = 0;
        DateTimeOffset? oldestWaiting = null;
        string? oldestTenant = null;
        var since = now.AddMinutes(-15);
        using var scope = scopes.CreateScope();
        var master = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
        var tenantDbs = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var tenants = await master.Tenants.AsNoTracking().Where(t => t.IsActive).Select(t => new { t.Name, t.SchemaName }).ToListAsync(ct);
        foreach (var t in tenants)
        {
            try
            {
                await using var db = tenantDbs.Create(t.SchemaName);
                queued += await db.CallStateHistory.Where(e => e.State == CallHistoryState.InQueue && e.EnteredAt >= since)
                    .Select(e => e.CallRecordId).Distinct().CountAsync(ct);
                abandoned += await db.CallStateHistory.Where(e => e.State == CallHistoryState.Abandoned && e.AbandonType == CallAbandonType.InQueue && e.EnteredAt >= since)
                    .Select(e => e.CallRecordId).Distinct().CountAsync(ct);
                var waiting = await OldestWaitingAsync(db, ct);
                if (waiting is { } w && (oldestWaiting is null || w < oldestWaiting)) { oldestWaiting = w; oldestTenant = t.Name; }
                exportsFailed += await db.ExportDeliveries.CountAsync(d => d.Status == ExportDeliveryStatus.Failed && !d.IsTest && d.QueuedAt >= now.AddHours(-24), ct);
                callbacksLate += await db.ScheduledCallbacks.CountAsync(c => c.Status == ScheduledCallbackStatus.Scheduled
                    && c.ScheduledFor < now.AddMinutes(-5) && c.ExpiresAt > now, ct);
                mergesWaiting += await db.RecordingMergeJobs.CountAsync(j => j.Status == RecordingMergeJobStatus.Pending && j.CreatedAt < now.AddMinutes(-30), ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Health: couldn't measure tenant {Schema}", t.SchemaName); }
        }
        var wait = oldestWaiting is { } o ? Math.Round((now - o).TotalSeconds) : 0;
        return
        [
            queued < 10
                ? new("abandon_rate", queued == 0 ? null : Math.Round(100.0 * abandoned / queued), $"{abandoned} of {queued} queued callers hung up (judged at 10+)", HealthStatus.Unknown)
                : new("abandon_rate", Math.Round(100.0 * abandoned / queued), $"{abandoned} of {queued} queued callers hung up"),
            new("queue_wait", wait, oldestWaiting is null ? "Nobody waiting" : $"Longest wait is in {oldestTenant}"),
            new("exports_failed", exportsFailed, exportsFailed == 0 ? "None" : "See the tenant's Data Exports page"),
            new("callbacks_late", callbacksLate, callbacksLate == 0 ? "None" : "Scheduled callbacks past due"),
            new("recordings_backlog", mergesWaiting, mergesWaiting == 0 ? "None" : "Recording merges waiting over 30 minutes"),
        ];
    }

    /// <summary>When the longest-waiting caller now in queue entered it (latest state per call = in queue).</summary>
    private static async Task<DateTimeOffset?> OldestWaitingAsync(TenantDbContext db, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        const string sql = """
            WITH latest AS (
                SELECT DISTINCT ON (call_record_id) call_record_id, state, entered_at
                FROM call_state_history
                WHERE entered_at > now() - interval '1 day'
                ORDER BY call_record_id, sequence DESC
            )
            SELECT min(entered_at) FROM latest WHERE state = @inQueue
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("inQueue", CallHistoryState.InQueue);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DateTime dt ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)) : v as DateTimeOffset?;
    }

    private async Task PruneAsync(IServiceScope scope, CancellationToken ct)
    {
        _lastPrune = DateTimeOffset.UtcNow;
        var db = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var removed = await db.HealthSamples.Where(s => s.At < cutoff).ExecuteDeleteAsync(ct);
        var incidents = await db.HealthIncidents.Where(i => i.ResolvedAt != null && i.ResolvedAt < DateTimeOffset.UtcNow.AddDays(-90)).ExecuteDeleteAsync(ct);
        if (removed + incidents > 0) logger.LogInformation("Health: pruned {Samples} samples and {Incidents} old incidents", removed, incidents);
    }
}
