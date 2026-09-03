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
/// Drives the <see cref="ScheduledCallback"/> lifecycle (ARCHITECTURE.md §16). Runs every 30 s
/// and, for every active tenant:
///
///   1. Expiry sweep — <c>scheduled</c> rows whose attempt window has closed with no successful
///      contact → <c>expired</c>.
///   2. Due sweep — <c>scheduled</c> rows whose booked time has arrived → place the outbound leg
///      (ESL <c>originate ... &amp;park()</c> to the caller's number, carrying cc_did / cc_callback_id /
///      cc_target_flow_id so the answered leg resolves the tenant and runs the designated flow),
///      mark <c>attempted</c>. The connected call record is created by the ESL park handler.
///   3. Stale-attempt sweep — only when <c>ScheduledCallbacks:AutoResolveAttemptedAfterSeconds</c>
///      &gt; 0 (default off): an <c>attempted</c> row with no completion signal after that many
///      seconds → <see cref="ScheduledCallback.MarkNoAnswer"/>.
///
/// The precise <c>completed</c> / <c>abandoned</c> signals come from real FreeSWITCH events on
/// the callback leg — that wiring lives in the API's ESL path
/// (<c>EslBackgroundService</c> → <see cref="IScheduledCallbackConnectionService"/>).
/// </summary>
public sealed class ScheduledCallbackProcessingService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ScheduledCallbackProcessingService> _logger;

    private readonly string _eslHost;
    private readonly int _eslPort;
    private readonly string _eslPassword;
    private readonly string _defaultGateway;
    private readonly int _autoResolveAfterSeconds;

    public ScheduledCallbackProcessingService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ScheduledCallbackProcessingService> logger)
    {
        _scopeFactory = scopeFactory;
        _config       = config;
        _logger       = logger;

        _eslHost           = config["FreeSWITCH:Host"] ?? config.GetSection("FreeSwitchEsl")["Host"] ?? "127.0.0.1";
        _eslPort           = int.TryParse(config["FreeSWITCH:EslPort"], out var p) ? p
                             : config.GetSection("FreeSwitchEsl").GetValue<int>("Port", 8021);
        _eslPassword       = config["FreeSWITCH:EslPassword"] ?? "ClueCon";
        _defaultGateway    = config["FreeSWITCH:DefaultGateway"] ?? "telnyx";
        _autoResolveAfterSeconds = int.TryParse(config["ScheduledCallbacks:AutoResolveAttemptedAfterSeconds"], out var s) ? s : 0;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ScheduledCallbackProcessingService started (gateway={Gateway}, autoResolveAfter={Secs}s).",
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
                _logger.LogError(ex, "Unhandled error in scheduled-callback processing cycle.");
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
                _logger.LogError(ex, "Error processing scheduled callbacks for tenant {TenantId} ({Subdomain}).",
                    tenant.Id, tenant.Subdomain);
            }
        }
    }

    private async Task ProcessTenantAsync(Guid tenantId, string schemaName, string subdomain, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory  = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory>();
        var platformDb = scope.ServiceProvider.GetRequiredService<ContactConnectionDbContext>();

        // Resolved lazily — only the opt-in stale-attempt sweep needs it, and ICallStateHistoryRecorder
        // pulls in IDashboardNotifier, which the Worker host does not register (project_worker_dev_boot).
        ICallStateHistoryRecorder? stateRecorder = null;
        ICallStateHistoryRecorder StateRecorder() =>
            stateRecorder ??= scope.ServiceProvider.GetRequiredService<ICallStateHistoryRecorder>();

        await using var db = dbFactory.Create(schemaName);

        var now = DateTimeOffset.UtcNow;

        var pending = await db.ScheduledCallbacks
            .Where(c => c.Status == ScheduledCallbackStatus.Scheduled
                        || c.Status == ScheduledCallbackStatus.Attempted)
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
                    callback.MarkExpired("Attempt window closed without a successful contact.");
                    expired++;
                    continue;
                }

                // ── 3. Stale attempt (opt-in) ──────────────────────────────
                if (callback.Status == ScheduledCallbackStatus.Attempted)
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
                            detail: $"Scheduled callback abandoned after {callback.AttemptCount} attempt(s)",
                            abandonType: CallAbandonType.CallbackAbandon, ct: ct);
                    }
                    continue;
                }

                // ── 2. Due — place the outbound leg ────────────────────────
                if (!callback.IsDue(now)) continue;

                // cc_did resolves the tenant/campaign for the answered leg (via the DID park
                // path) and doubles as the outbound caller ID. Use the exact DID the caller
                // dialed (frozen on the row); fall back to the campaign routing table (prefer a
                // real E.164 over a bare/placeholder) only for pre-Dnis rows.
                var did = callback.Dnis;
                if (string.IsNullOrEmpty(did))
                {
                    var routeCampaign = callback.TargetCampaignId ?? callback.CampaignId;
                    var dids = await platformDb.PhoneNumberRoutings
                        .Where(r => r.IsActive && r.CampaignId == routeCampaign)
                        .Select(r => r.Number)
                        .ToListAsync(ct);
                    did = dids.FirstOrDefault(n => n.StartsWith('+')) ?? dids.FirstOrDefault();
                }

                if (string.IsNullOrEmpty(did))
                {
                    _logger.LogWarning(
                        "ScheduledCallback {CallbackId}: no DNIS on the row and campaign {CampaignId} has no active " +
                        "provisioned DID — cannot resolve a route. Leaving it scheduled.",
                        callback.Id, callback.CampaignId);
                    continue;
                }

                var callerId      = callback.CallerIdOverride ?? did;
                var campaignVar   = callback.TargetCampaignId ?? callback.CampaignId;
                var targetFlowVar = callback.TargetFlowId is { } tf ? $"cc_target_flow_id={tf}," : "";

                // No call record yet — the DID park handler creates the connected record on answer
                // and links it via IScheduledCallbackConnectionService.MarkConnectedAsync.
                callback.MarkAttempted();
                await db.SaveChangesAsync(ct);

                esl ??= await ConnectEslAsync(ct);
                var digits  = new string(callback.CallbackNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());
                var command =
                    $"originate {{origination_caller_id_number={callerId}," +
                    $"cc_did={did},cc_callback_id={callback.Id},{targetFlowVar}" +
                    $"cc_tenant_id={tenantId},cc_tenant_schema={schemaName},cc_tenant_subdomain={subdomain}," +
                    $"cc_campaign_id={campaignVar},ignore_early_media=true," +
                    $"originate_timeout=45}}sofia/gateway/{_defaultGateway}/{digits} &park()";

                await esl.SendBgApiAsync(command, ct);
                placed++;

                _logger.LogInformation(
                    "ScheduledCallback {CallbackId} attempt {Attempt}/{Max}: originating {Number} " +
                    "(DID {Did}, targetFlow {Flow}).",
                    callback.Id, callback.AttemptCount, callback.MaxAttempts,
                    callback.CallbackNumber, did, callback.TargetFlowId?.ToString() ?? "(campaign default)");
            }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            if (esl is not null) await esl.DisposeAsync();
        }

        if (expired + placed + resolved > 0)
            _logger.LogInformation(
                "ScheduledCallbacks [{Subdomain}]: {Placed} placed, {Expired} expired, {Resolved} stale-resolved.",
                subdomain, placed, expired, resolved);
    }

    private async Task<FreeSwitchEslClient> ConnectEslAsync(CancellationToken ct)
    {
        var client = new FreeSwitchEslClient(_eslHost, _eslPort, _eslPassword, _logger);
        await client.ConnectAsync(ct);
        return client;
    }
}
