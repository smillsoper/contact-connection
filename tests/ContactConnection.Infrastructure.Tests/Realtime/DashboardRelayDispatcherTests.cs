using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Infrastructure.Realtime;
using Moq;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Realtime;

/// <summary>
/// Covers <see cref="DashboardRelayDispatcher"/> — the API side of the Worker→API dashboard
/// relay. Each relayed message must map back to exactly the <see cref="IDashboardNotifier"/>
/// call the Worker made; an unknown kind is ignored (forward-compat), garbage doesn't throw.
/// </summary>
public class DashboardRelayDispatcherTests
{
    private static readonly Guid Tenant   = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Agent    = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Campaign = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void TryParse_Garbage_ReturnsNull()
    {
        Assert.Null(DashboardRelayDispatcher.TryParse("not json"));
        Assert.Null(DashboardRelayDispatcher.TryParse("{ unterminated"));
    }

    [Fact]
    public async Task DispatchAsync_AgentState_CallsNotifier()
    {
        var n = new Mock<IDashboardNotifier>();
        var since = DateTimeOffset.UtcNow;
        var msg = new DashboardRelayMessage
        {
            Kind = DashboardRelayKind.AgentState, TenantId = Tenant, AgentId = Agent,
            StateCode = "available", Label = "Available", Since = since,
        };

        var handled = await DashboardRelayDispatcher.DispatchAsync(msg, n.Object);

        Assert.True(handled);
        n.Verify(x => x.NotifyAgentStateChangedAsync(
            Tenant, Agent, "available", "Available", since, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_CallState_CallsNotifier()
    {
        var n = new Mock<IDashboardNotifier>();
        var msg = new DashboardRelayMessage
        {
            Kind = DashboardRelayKind.CallState, TenantId = Tenant, CampaignId = Campaign, State = "abandoned",
        };

        Assert.True(await DashboardRelayDispatcher.DispatchAsync(msg, n.Object));
        n.Verify(x => x.NotifyCallStateChangedAsync(
            Tenant, Campaign, "abandoned", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_AgentRegistration_CallsNotifier()
    {
        var n = new Mock<IDashboardNotifier>();
        var since = DateTimeOffset.UtcNow;
        var msg = new DashboardRelayMessage
        {
            Kind = DashboardRelayKind.AgentRegistration, TenantId = Tenant, AgentId = Agent,
            Registered = true, Since = since,
        };

        Assert.True(await DashboardRelayDispatcher.DispatchAsync(msg, n.Object));
        n.Verify(x => x.NotifyAgentRegistrationChangedAsync(
            Tenant, Agent, true, since, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_Voicemail_CallsNotifier()
    {
        var n = new Mock<IDashboardNotifier>();
        var vmId = Guid.NewGuid();
        var recId = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var msg = new DashboardRelayMessage
        {
            Kind = DashboardRelayKind.Voicemail, TenantId = Tenant, CampaignId = Campaign,
            VoicemailId = vmId, CallRecordId = recId, CallerId = "+15551234567",
            DurationSeconds = 12, CreatedAt = created,
        };

        Assert.True(await DashboardRelayDispatcher.DispatchAsync(msg, n.Object));
        n.Verify(x => x.NotifyVoicemailReceivedAsync(
            Tenant, Campaign, vmId, recId, "+15551234567", 12, created, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_ScheduledCallback_CallsNotifier()
    {
        var n = new Mock<IDashboardNotifier>();
        var msg = new DashboardRelayMessage
        {
            Kind = DashboardRelayKind.ScheduledCallback, TenantId = Tenant, CampaignId = Campaign,
            Change = "attempted",
        };

        Assert.True(await DashboardRelayDispatcher.DispatchAsync(msg, n.Object));
        n.Verify(x => x.NotifyScheduledCallbackChangedAsync(
            Tenant, Campaign, "attempted", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_UnknownKind_ReturnsFalse_NoCalls()
    {
        var n = new Mock<IDashboardNotifier>(MockBehavior.Strict);
        var msg = new DashboardRelayMessage { Kind = "something_new", TenantId = Tenant };

        Assert.False(await DashboardRelayDispatcher.DispatchAsync(msg, n.Object));
    }
}
