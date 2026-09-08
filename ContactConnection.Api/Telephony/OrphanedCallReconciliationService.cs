using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Runs <see cref="IOrphanedCallReconciler"/> once, a short delay after startup, to close calls
/// stranded in a non-terminal state by a previous process that stopped mid-call (hard restart,
/// crash, dev Ctrl-C with no graceful CHANNEL_HANGUP). Without it, each such event permanently
/// leaves a "phantom active call" on the supervisor dashboard — cleaned up by hand in Sessions
/// 118 and 119 before this existed. The sweep is idempotent and safe on every boot.
///
/// Config (all optional):
///   Telephony:OrphanReconciliation:Enabled              (bool, default true)
///   Telephony:OrphanReconciliation:StartupDelaySeconds  (int,  default 20)
///   Telephony:OrphanReconciliation:MinAgeMinutes        (int,  default 15 — read by the reconciler)
/// </summary>
public sealed class OrphanedCallReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<OrphanedCallReconciliationService> _logger;

    public OrphanedCallReconciliationService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<OrphanedCallReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _config       = config;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Telephony:OrphanReconciliation:Enabled", true))
        {
            _logger.LogInformation("Orphaned-call reconciliation disabled by configuration — skipping");
            return;
        }

        var delaySeconds = _config.GetValue("Telephony:OrphanReconciliation:StartupDelaySeconds", 20);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, delaySeconds)), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reconciler  = scope.ServiceProvider.GetRequiredService<IOrphanedCallReconciler>();
            var result      = await reconciler.RunAsync(stoppingToken);

            _logger.LogInformation(
                "Orphaned-call reconciliation complete — {Records} call record(s) and {Timelines} state timeline(s) " +
                "closed across {Tenants} tenant(s)",
                result.CallRecordsClosed, result.StateTimelinesClosed, result.TenantsScanned);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orphaned-call reconciliation sweep failed");
        }
    }
}
