using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Realtime;
using ContactConnection.Infrastructure.Tests.Credentials;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Realtime;

/// <summary>
/// Covers <see cref="RedisPublishingDashboardNotifier"/> against a real Redis (see
/// <see cref="RedisFixture"/>): each notifier call must land on
/// <see cref="DashboardRelayMessage.RedisChannel"/> as a message that
/// <see cref="DashboardRelayDispatcher.TryParse"/> round-trips with the right fields — i.e. the
/// publish half and the wire format the API subscriber depends on, proven together.
/// </summary>
[Collection("Redis")]
public class RedisPublishingDashboardNotifierTests(RedisFixture fixture)
{
    private async Task<DashboardRelayMessage> CaptureAsync(Func<RedisPublishingDashboardNotifier, Task> act)
    {
        var sub = fixture.Connection.GetSubscriber();
        var channel = RedisChannel.Literal(DashboardRelayMessage.RedisChannel);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await sub.SubscribeAsync(channel, (_, value) => tcs.TrySetResult(value!));
        try
        {
            // Redis pub/sub has no buffering — make sure the subscription is live first.
            await Task.Delay(150);
            await act(new RedisPublishingDashboardNotifier(fixture.Connection));

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(completed == tcs.Task, "no relay message received within 5s");

            var parsed = DashboardRelayDispatcher.TryParse(await tcs.Task);
            Assert.NotNull(parsed);
            return parsed!;
        }
        finally
        {
            await sub.UnsubscribeAsync(channel);
        }
    }

    [Fact]
    public async Task NotifyScheduledCallbackChanged_PublishesParseableMessage()
    {
        var tenant = Guid.NewGuid();
        var campaign = Guid.NewGuid();

        var msg = await CaptureAsync(n =>
            n.NotifyScheduledCallbackChangedAsync(tenant, campaign, "attempted"));

        Assert.Equal(DashboardRelayKind.ScheduledCallback, msg.Kind);
        Assert.Equal(tenant, msg.TenantId);
        Assert.Equal(campaign, msg.CampaignId);
        Assert.Equal("attempted", msg.Change);
    }

    [Fact]
    public async Task NotifyCallStateChanged_PublishesParseableMessage()
    {
        var tenant = Guid.NewGuid();
        var campaign = Guid.NewGuid();

        var msg = await CaptureAsync(n => n.NotifyCallStateChangedAsync(tenant, campaign, "abandoned"));

        Assert.Equal(DashboardRelayKind.CallState, msg.Kind);
        Assert.Equal(tenant, msg.TenantId);
        Assert.Equal(campaign, msg.CampaignId);
        Assert.Equal("abandoned", msg.State);
    }

    [Fact]
    public async Task NotifyAgentStateChanged_PublishesParseableMessage()
    {
        var tenant = Guid.NewGuid();
        var agent = Guid.NewGuid();
        var since = DateTimeOffset.UtcNow;

        var msg = await CaptureAsync(n =>
            n.NotifyAgentStateChangedAsync(tenant, agent, "acw", "After Call Work", since));

        Assert.Equal(DashboardRelayKind.AgentState, msg.Kind);
        Assert.Equal(tenant, msg.TenantId);
        Assert.Equal(agent, msg.AgentId);
        Assert.Equal("acw", msg.StateCode);
        Assert.Equal("After Call Work", msg.Label);
        Assert.Equal(since, msg.Since);
    }

    [Fact]
    public async Task RoundTrip_ThroughDispatcher_HitsTheRealNotifierMethod()
    {
        var tenant = Guid.NewGuid();
        var campaign = Guid.NewGuid();
        var msg = await CaptureAsync(n =>
            n.NotifyScheduledCallbackChangedAsync(tenant, campaign, "expired"));

        var notifier = new Mock<IDashboardNotifier>();
        var handled = await DashboardRelayDispatcher.DispatchAsync(msg, notifier.Object);

        Assert.True(handled);
        notifier.Verify(x => x.NotifyScheduledCallbackChangedAsync(
            tenant, campaign, "expired", It.IsAny<CancellationToken>()), Times.Once);
    }
}
