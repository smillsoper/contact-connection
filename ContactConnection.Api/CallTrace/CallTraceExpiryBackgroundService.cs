using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;

namespace ContactConnection.Api.CallTrace;

/// <summary>
/// Ticks every second and stops any trace subscription that has exceeded its duration cap
/// (for duration-mode traces) or the hard backstop duration ceiling (for count-mode traces
/// that never reach their count). State lives in Redis via ICallTraceSubscriptionRegistry,
/// so this is safe to run on every API instance — a duplicate stop is harmless.
/// </summary>
public sealed class CallTraceExpiryBackgroundService(
    ICallTraceSubscriptionRegistry registry,
    IServiceScopeFactory scopeFactory,
    ILogger<CallTraceExpiryBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "CallTraceExpiryBackgroundService: unhandled error during sweep");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var tenantIds = await registry.GetActiveTenantsAsync(ct);
        var now = DateTimeOffset.UtcNow;

        using var scope = scopeFactory.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ICallTraceNotifier>();

        foreach (var tenantId in tenantIds)
        {
            var subscriptions = await registry.GetActiveSubscriptionsAsync(tenantId, ct);
            foreach (var sub in subscriptions)
            {
                var cap = sub.CaptureMode == CallTraceCaptureMode.Duration
                    ? TimeSpan.FromMinutes(sub.CaptureValue)
                    : CallTraceLimits.MaxCaptureDuration;

                if (now - sub.StartedAt < cap) continue;

                await registry.StopTraceAsync(sub.SubscriptionId, "duration-elapsed", ct);
                await notifier.NotifyTraceStoppedAsync(sub.SubscriptionId, "duration-elapsed", ct);
            }
        }
    }
}
