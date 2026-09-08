using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Realtime;
using StackExchange.Redis;

namespace ContactConnection.Api.Realtime;

/// <summary>
/// Bridges the Worker → API supervisor-dashboard relay. Subscribes to
/// <see cref="DashboardRelayMessage.RedisChannel"/> and re-emits each message through this
/// instance's real hub-backed <see cref="IDashboardNotifier"/> so a change made in the Worker
/// (which has no SignalR hub) still reaches supervisors' browsers live.
///
/// Every API instance subscribes; SignalR's own Redis backplane then fans the resulting group
/// send out to whichever instance actually holds each supervisor connection, so duplicate
/// delivery isn't a concern (the message is idempotent — the widgets re-fetch on it).
/// </summary>
public sealed class DashboardRelaySubscriber : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDashboardNotifier _notifier;
    private readonly ILogger<DashboardRelaySubscriber> _logger;

    public DashboardRelaySubscriber(
        IConnectionMultiplexer redis,
        IDashboardNotifier notifier,
        ILogger<DashboardRelaySubscriber> logger)
    {
        _redis    = redis;
        _notifier = notifier;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = RedisChannel.Literal(DashboardRelayMessage.RedisChannel);
        var queue   = await _redis.GetSubscriber().SubscribeAsync(channel);

        queue.OnMessage(async msg =>
        {
            try
            {
                var relay = DashboardRelayDispatcher.TryParse(msg.Message.ToString());
                if (relay is null)
                {
                    _logger.LogWarning("DashboardRelaySubscriber: could not parse relay message");
                    return;
                }

                var handled = await DashboardRelayDispatcher.DispatchAsync(relay, _notifier, stoppingToken);
                if (handled)
                    _logger.LogInformation(
                        "DashboardRelaySubscriber: relayed '{Kind}' for tenant {TenantId} to supervisor dashboards",
                        relay.Kind, relay.TenantId);
                else
                    _logger.LogWarning(
                        "DashboardRelaySubscriber: unrecognized relay kind '{Kind}' — ignored", relay.Kind);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DashboardRelaySubscriber: error handling a relay message");
            }
        });

        _logger.LogInformation(
            "DashboardRelaySubscriber listening on '{Channel}'", DashboardRelayMessage.RedisChannel);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _redis.GetSubscriber().UnsubscribeAsync(channel);
        }
    }
}
