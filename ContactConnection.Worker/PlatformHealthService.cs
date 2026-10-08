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

        // ── Integrations + things that expire (phase 2) ──
        results.AddRange(await MeasureIntegrationsAsync(ct));
        results.Add(await MeasureCredentialsAsync(ct));
        results.Add(await MeasureTlsAsync(ct));
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
        int queued = 0, abandoned = 0, exportsFailed = 0, callbacksLate = 0, mergesWaiting = 0, payTotal = 0, payErrors = 0;
        string? payLastError = null;
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
                var pays = await db.PaymentTransactions.AsNoTracking().Where(x => x.CreatedAt >= now.AddHours(-1))
                    .Select(x => new { x.Status, x.ResponseReasonText }).ToListAsync(ct);
                payTotal += pays.Count;
                payErrors += pays.Count(x => x.Status == PaymentTransactionStatus.Error);
                payLastError ??= pays.LastOrDefault(x => x.Status == PaymentTransactionStatus.Error)?.ResponseReasonText;
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
            payTotal < 5
                ? new("payments", payTotal == 0 ? null : Math.Round(100.0 * payErrors / payTotal), $"{payErrors} error(s) in {payTotal} attempt(s) (judged at 5+)", HealthStatus.Unknown)
                : new("payments", Math.Round(100.0 * payErrors / payTotal), payErrors == 0 ? $"{payTotal} attempts, no gateway errors" : $"{payErrors} of {payTotal}; last: {payLastError}"),
        ];
    }

    /// <summary>Error rates from the integrations' own reports (RedisIntegrationHealth) over the last hour.</summary>
    private async Task<List<HealthResult>> MeasureIntegrationsAsync(CancellationToken ct)
    {
        var hour = await RedisIntegrationHealth.ReadHourAsync(redis);
        var results = new List<HealthResult>();

        // Client APIs: the worst one with enough calls to judge.
        var apis = hour.Where(h => h.Source.StartsWith("api:") && h.Ok + h.Fail > 0).ToList();
        var judged = apis.Where(a => a.Ok + a.Fail >= 5).OrderByDescending(a => (double)a.Fail / (a.Ok + a.Fail)).ToList();
        var names = apis.Count == 0 ? new Dictionary<string, string>() : await ApiNamesAsync(apis.Select(a => a.Source[4..]).ToList(), ct);
        string Name(string source) => names.GetValueOrDefault(source[4..]) ?? "an API";
        if (judged.Count == 0)
            results.Add(new("client_apis", null, apis.Count == 0 ? "No API calls in the last hour"
                : $"{apis.Sum(a => a.Ok + a.Fail)} call(s), {apis.Sum(a => a.Fail)} failed (judged per API at 5+)", HealthStatus.Unknown));
        else
        {
            var w = judged[0];
            var pct = Math.Round(100.0 * w.Fail / (w.Ok + w.Fail));
            results.Add(new("client_apis", pct, w.Fail == 0 ? $"{apis.Sum(a => a.Ok + a.Fail)} calls across {apis.Count} API(s), none failed"
                : $"{Name(w.Source)}: {w.Fail} of {w.Ok + w.Fail} failed — {w.LastError}"));
        }
        var open = await RedisIntegrationHealth.OpenCircuitsAsync(redis);
        results.Add(open.Count == 0
            ? new("api_circuits", 0, "All closed", HealthStatus.Ok)
            : new("api_circuits", open.Count, $"Refusing calls to {string.Join(", ", open.Select(id => names.GetValueOrDefault(id) ?? id))}", HealthStatus.Critical));

        HealthResult Rate(string key, string prefix, string noun)
        {
            var rows = hour.Where(h => h.Source.StartsWith(prefix)).ToList();
            long ok = rows.Sum(r => r.Ok), fail = rows.Sum(r => r.Fail);
            var last = rows.Where(r => r.Fail > 0).Select(r => r.LastError).FirstOrDefault(e => e is not null);
            if (ok + fail < 5)
                return new(key, ok + fail == 0 ? null : Math.Round(100.0 * fail / (ok + fail)),
                    ok + fail == 0 ? $"No {noun} in the last hour" : $"{fail} of {ok + fail} {noun} failed (judged at 5+)", HealthStatus.Unknown);
            return new(key, Math.Round(100.0 * fail / (ok + fail)), fail == 0 ? $"{ok} {noun}, none failed" : $"{fail} of {ok + fail} {noun} failed — {last}");
        }
        results.Add(Rate("tax", "tax:", "tax calls"));
        results.Add(Rate("tts", "tts:", "prompts"));
        results.Add(Rate("stt", "stt:", "voice captures"));

        HealthResult Count(string key, string source, string none)
        {
            var row = hour.FirstOrDefault(h => h.Source == source);
            return row.Source is null || row.Fail == 0
                ? new(key, 0, row.Source is null ? none : $"{row.Ok} in the last hour, none failed")
                : new(key, row.Fail, $"{row.Fail} of {row.Ok + row.Fail} — {row.LastError}");
        }
        results.Add(Count("email", "email", "No email sent in the last hour"));
        results.Add(Count("stripe_webhooks", "stripe-webhook", "No webhooks in the last hour"));
        return results;
    }

    /// <summary>Client API definitions' names, platform-wide and per tenant.</summary>
    private async Task<Dictionary<string, string>> ApiNamesAsync(List<string> ids, CancellationToken ct)
    {
        var guids = ids.Select(i => Guid.TryParse(i, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        using var scope = scopes.CreateScope();
        var master = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
        var names = await master.PortalApiDefinitions.AsNoTracking().Where(d => guids.Contains(d.Id)).ToDictionaryAsync(d => d.Id.ToString(), d => d.Name, ct);
        if (names.Count == guids.Count) return names;
        var tenantDbs = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        foreach (var t in await master.Tenants.AsNoTracking().Where(t => t.IsActive).Select(t => new { t.Name, t.SchemaName }).ToListAsync(ct))
        {
            try
            {
                await using var db = tenantDbs.Create(t.SchemaName);
                foreach (var d in await db.TenantApiDefinitions.AsNoTracking().Where(d => guids.Contains(d.Id)).Select(d => new { d.Id, d.Name }).ToListAsync(ct))
                    names[d.Id.ToString()] = $"{d.Name} ({t.Name})";
            }
            catch (Exception ex) { logger.LogDebug(ex, "Health: API names for {Schema}", t.SchemaName); }
        }
        return names;
    }

    private (DateTimeOffset At, HealthResult Result)? _credentials, _tls;

    /// <summary>Soonest expiry among Key Vault secrets that have one set (names only — values are never read). Hourly.</summary>
    private async Task<HealthResult> MeasureCredentialsAsync(CancellationToken ct)
    {
        if (_credentials is { } c && DateTimeOffset.UtcNow - c.At < TimeSpan.FromHours(1)) return c.Result;
        HealthResult result;
        using var scope = scopes.CreateScope();
        var client = scope.ServiceProvider.GetService<Azure.Security.KeyVault.Secrets.SecretClient>();
        if (client is null) result = new("credentials", null, "No Key Vault configured", HealthStatus.Unknown);
        else
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                (string Name, DateTimeOffset Expires)? soonest = null;
                var withExpiry = 0;
                await foreach (var props in client.GetPropertiesOfSecretsAsync(ct))
                {
                    if (props.Enabled == false || props.ExpiresOn is not { } exp) continue;
                    withExpiry++;
                    if (soonest is null || exp < soonest.Value.Expires) soonest = (props.Name, exp);
                }
                result = soonest is { } s
                    ? new("credentials", Math.Floor((s.Expires - now).TotalDays), $"{s.Name} expires {s.Expires:yyyy-MM-dd} ({withExpiry} with expiry dates)")
                    : new("credentials", null, "No credentials have an expiry date set", HealthStatus.Ok);
            }
            catch (Exception ex) { result = new("credentials", null, $"Couldn't read Key Vault: {ex.Message}", HealthStatus.Unknown); }
        }
        _credentials = (DateTimeOffset.UtcNow, result);
        return result;
    }

    /// <summary>Days left on the TLS certificates of the configured hosts (Health:TlsHosts, comma-separated). Hourly.</summary>
    private async Task<HealthResult> MeasureTlsAsync(CancellationToken ct)
    {
        if (_tls is { } c && DateTimeOffset.UtcNow - c.At < TimeSpan.FromHours(1)) return c.Result;
        var hosts = (config["Health:TlsHosts"] ?? "contactconnection.io").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        (string Host, DateTime NotAfter)? soonest = null;
        var errors = new List<string>();
        foreach (var host in hosts)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(host, 443, timeout.Token);
                System.Security.Cryptography.X509Certificates.X509Certificate2? cert = null;
                await using var ssl = new System.Net.Security.SslStream(tcp.GetStream(), false, (_, cer, _, _) =>
                {
                    if (cer is not null) cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(cer);
                    return true;   // only reading the expiry
                });
                await ssl.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions { TargetHost = host }, timeout.Token);
                if (cert is not null && (soonest is null || cert.NotAfter < soonest.Value.NotAfter)) soonest = (host, cert.NotAfter.ToUniversalTime());
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { errors.Add($"{host}: {ex.Message}"); }
        }
        HealthResult result = soonest is { } s
            ? new("tls", Math.Floor((s.NotAfter - DateTime.UtcNow).TotalDays), $"{s.Host} valid until {s.NotAfter:yyyy-MM-dd}{(errors.Count > 0 ? $"; couldn't check {string.Join(", ", errors)}" : "")}")
            : new("tls", null, $"Couldn't read the certificate — {string.Join("; ", errors)}", HealthStatus.Warning);
        _tls = (DateTimeOffset.UtcNow, result);
        return result;
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
