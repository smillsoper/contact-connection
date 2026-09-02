using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.FreeSwitchEsl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Worker;

/// <summary>
/// Drives the <see cref="Callback"/> lifecycle (ARCHITECTURE.md §16). Runs every 30 s and, for
/// every active tenant:
///
///   1. Expiry sweep — <c>scheduled</c>/<c>requested</c> callbacks whose window has closed with
///      no attempt → <c>expired</c>.
///   2. Due sweep — <c>scheduled</c> callbacks whose window is open → place the outbound leg
///      (create the callback CallRecord, mark <c>attempted</c>, ESL <c>originate</c> to the
///      caller routed into the campaign's callback dialplan extension).
///   3. Stale-attempt sweep — only when <c>Callbacks:AutoResolveAttemptedAfterSeconds</c> &gt; 0
///      (default off): an <c>attempted</c> callback with no completion signal after that many
///      seconds → <see cref="Callback.MarkNoAnswer"/> (back to <c>scheduled</c> while retries
///      remain, else <c>abandoned</c> + a <c>callback_abandon</c> call-state-history row).
///
/// The precise <c>completed</c> / <c>abandoned</c> signals come from real FreeSWITCH answer/
/// hangup events on the callback leg — that wiring lives in the ESL event path
/// (<see cref="FreeSwitchEslService"/>) and is added in a follow-up session; until then the
/// stale-attempt sweep is the only resolution and stays opt-in so it never records a false
/// abandon against a call that actually connected.
/// </summary>
public sealed class CallbackProcessingService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<CallbackProcessingService> _logger;

    private readonly string _eslHost;
    private readonly int _eslPort;
    private readonly string _eslPassword;
    private readonly string _defaultGateway;
    private readonly int _autoResolveAfterSeconds;

    public CallbackProcessingService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<CallbackProcessingService> logger)
    {
        _scopeFactory = scopeFactory;
        _config       = config;
        _logger       = logger;

        _eslHost           = config["FreeSWITCH:Host"] ?? config.GetSection("FreeSwitchEsl")["Host"] ?? "127.0.0.1";
        _eslPort           = int.TryParse(config["FreeSWITCH:EslPort"], out var p) ? p
                             : config.GetSection("FreeSwitchEsl").GetValue<int>("Port", 8021);
        _eslPassword       = config["FreeSWITCH:EslPassword"] ?? "ClueCon";
        _defaultGateway    = config["FreeSWITCH:DefaultGateway"] ?? "telnyx";
        _autoResolveAfterSeconds = int.TryParse(config["Callbacks:AutoResolveAttemptedAfterSeconds"], out var s) ? s : 0;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CallbackProcessingService started (gateway={Gateway}, autoResolveAfter={Secs}s).",
            _defaultGateway, _autoResolveAfterSeconds);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessAllTenantsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in callback processing cycle.");
            }
        }
    }

    private async Task ProcessAllTenantsAsync(CancellationToken ct)
    {
        using var platformScope = _scopeFactory.CreateScope();
        var platformDb = platformScope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();
        var tenants    = await platformDb.Tenants.Where(t => t.IsActive).ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            try
            {
                await ProcessTenantAsync(tenant.Id, tenant.SchemaName, tenant.Subdomain, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing callbacks for tenant {TenantId} ({Subdomain}).",
                    tenant.Id, tenant.Subdomain);
            }
        }
    }

    private async Task ProcessTenantAsync(Guid tenantId, string schemaName, string subdomain, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory      = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var platformDb     = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();

        // Resolved lazily — only the opt-in stale-attempt sweep needs it, and ICallStateHistoryRecorder
        // pulls in IDashboardNotifier, which the Worker host does not register (see project_worker_dev_boot).
        // Keeping the resolution out of the default path lets CallbackProcessingService run in the Worker
        // without that registration.
        ICallStateHistoryRecorder? stateRecorder = null;
        ICallStateHistoryRecorder StateRecorder() =>
            stateRecorder ??= scope.ServiceProvider.GetRequiredService<ICallStateHistoryRecorder>();

        await using var db = dbFactory.Create(schemaName);

        var now = DateTimeOffset.UtcNow;

        // Cheap pre-filter: any non-terminal callback that could need action this tick.
        var pending = await db.Callbacks
            .Where(c => c.Status == CallbackStatus.Requested
                        || c.Status == CallbackStatus.Scheduled
                        || c.Status == CallbackStatus.Attempted)
            .OrderBy(c => c.ScheduledFor)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        var expired  = 0;
        var placed   = 0;
        var resolved = 0;

        FreeSwitchEslClient? esl = null;
        try
        {
            foreach (var callback in pending)
            {
                // ── 1. Expiry ──────────────────────────────────────────────
                if (callback.IsExpired(now))
                {
                    callback.MarkExpired("Window closed without an attempt.");
                    expired++;
                    continue;
                }

                // ── 3. Stale attempt (opt-in) ──────────────────────────────
                if (callback.Status == CallbackStatus.Attempted)
                {
                    if (_autoResolveAfterSeconds <= 0) continue;
                    if (callback.LastAttemptAt is not { } last) continue;
                    if ((now - last).TotalSeconds < _autoResolveAfterSeconds) continue;

                    var abandoned = callback.MarkNoAnswer(
                        $"No completion signal within {_autoResolveAfterSeconds}s of attempt {callback.AttemptCount}.");
                    resolved++;

                    if (abandoned)
                    {
                        await StateRecorder().RecordAsync(
                            tenantId, schemaName, callback.CallRecordId,
                            CallHistoryState.Abandoned, callback.CampaignId, agentId: null,
                            detail: $"Callback abandoned after {callback.AttemptCount} attempt(s)",
                            abandonType: CallAbandonType.CallbackAbandon, ct: ct);
                    }
                    continue;
                }

                // ── 2. Due — place the outbound leg ────────────────────────
                if (!callback.IsDue(now)) continue;

                // cc_did routes the answered callback leg back to the campaign via the normal DID
                // park path, and doubles as the outbound caller ID. Use the exact DID the caller
                // dialed (frozen on the row). Fall back to the campaign's routing table only for
                // rows created before Dnis was captured — preferring a real E.164 over a
                // bare/placeholder number the trunk would reject.
                var did = callback.Dnis;
                if (string.IsNullOrEmpty(did))
                {
                    var dids = await platformDb.PhoneNumberRoutings
                        .Where(r => r.IsActive && r.CampaignId == callback.CampaignId)
                        .Select(r => r.Number)
                        .ToListAsync(ct);
                    did = dids.FirstOrDefault(n => n.StartsWith('+')) ?? dids.FirstOrDefault();
                }

                if (string.IsNullOrEmpty(did))
                {
                    _logger.LogWarning(
                        "Callback {CallbackId}: no DNIS on the row and campaign {CampaignId} has no active provisioned " +
                        "DID — cannot route the callback back into the queue. Leaving it scheduled.",
                        callback.Id, callback.CampaignId);
                    continue;
                }

                // Blank override = present the DID the caller dialed (most recognizable). A node
                // override is already resolved to a literal at request time.
                var callerId = callback.CallerIdOverride ?? did;

                // No call record yet — the DID park handler creates the connected record on answer
                // and links it back via ICallbackConnectionService.MarkConnectedAsync.
                callback.MarkAttempted();
                await db.SaveChangesAsync(ct);

                esl ??= await ConnectEslAsync(ct);
                var digits  = new string(callback.CallbackNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());
                var command =
                    $"originate {{origination_caller_id_number={callerId}," +
                    $"cc_did={did},cc_callback_id={callback.Id}," +
                    $"cc_tenant_id={tenantId},cc_tenant_schema={schemaName},cc_tenant_subdomain={subdomain}," +
                    $"cc_campaign_id={callback.CampaignId},ignore_early_media=true," +
                    $"originate_timeout=45}}sofia/gateway/{_defaultGateway}/{digits} &park()";

                await esl.SendBgApiAsync(command, ct);
                placed++;

                _logger.LogInformation(
                    "Callback {CallbackId} attempt {Attempt}/{Max}: originating {Number} for campaign {CampaignId} " +
                    "(DID {Did}).",
                    callback.Id, callback.AttemptCount, callback.MaxAttempts,
                    callback.CallbackNumber, callback.CampaignId, did);
            }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            if (esl is not null) await esl.DisposeAsync();
        }

        if (expired + placed + resolved > 0)
            _logger.LogInformation(
                "Callbacks [{Subdomain}]: {Placed} placed, {Expired} expired, {Resolved} stale-resolved.",
                subdomain, placed, expired, resolved);
    }

    private async Task<FreeSwitchEslClient> ConnectEslAsync(CancellationToken ct)
    {
        var client = new FreeSwitchEslClient(_eslHost, _eslPort, _eslPassword, _logger);
        await client.ConnectAsync(ct);
        return client;
    }
}
