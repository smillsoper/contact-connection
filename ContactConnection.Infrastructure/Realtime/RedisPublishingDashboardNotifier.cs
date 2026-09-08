using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;
using StackExchange.Redis;

namespace ContactConnection.Infrastructure.Realtime;

/// <summary>
/// <see cref="IDashboardNotifier"/> for a host with no SignalR hub (the Worker). Every call is
/// turned into a <see cref="DashboardRelayMessage"/> and published to
/// <see cref="DashboardRelayMessage.RedisChannel"/>; the API's <c>DashboardRelaySubscriber</c>
/// receives it and re-emits it through the real hub-backed notifier.
///
/// Fire-and-forget by design — a dashboard push is a best-effort live nicety, never a source of
/// truth (the widgets re-fetch on reconnect). A publish failure is swallowed rather than allowed
/// to break the Worker job that triggered it.
/// </summary>
public sealed class RedisPublishingDashboardNotifier : IDashboardNotifier
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;

    public RedisPublishingDashboardNotifier(IConnectionMultiplexer redis) => _redis = redis;

    private Task PublishAsync(DashboardRelayMessage message)
    {
        try
        {
            var json = JsonSerializer.Serialize(message, JsonOpts);
            return _redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal(DashboardRelayMessage.RedisChannel), json);
        }
        catch
        {
            // Best-effort — never let a dropped dashboard push fail the caller.
            return Task.CompletedTask;
        }
    }

    public Task NotifyAgentStateChangedAsync(
        Guid tenantId, Guid agentId, string stateCode, string label, DateTimeOffset since,
        CancellationToken ct = default) =>
        PublishAsync(new DashboardRelayMessage
        {
            Kind = DashboardRelayKind.AgentState,
            TenantId = tenantId, AgentId = agentId,
            StateCode = stateCode, Label = label, Since = since,
        });

    public Task NotifyCallStateChangedAsync(
        Guid tenantId, Guid campaignId, string state, CancellationToken ct = default) =>
        PublishAsync(new DashboardRelayMessage
        {
            Kind = DashboardRelayKind.CallState,
            TenantId = tenantId, CampaignId = campaignId, State = state,
        });

    public Task NotifyAgentRegistrationChangedAsync(
        Guid tenantId, Guid agentId, bool registered, DateTimeOffset? since,
        CancellationToken ct = default) =>
        PublishAsync(new DashboardRelayMessage
        {
            Kind = DashboardRelayKind.AgentRegistration,
            TenantId = tenantId, AgentId = agentId, Registered = registered, Since = since,
        });

    public Task NotifyVoicemailReceivedAsync(
        Guid tenantId, Guid campaignId, Guid voicemailId, Guid callRecordId, string? callerId,
        int durationSeconds, DateTimeOffset createdAt, CancellationToken ct = default) =>
        PublishAsync(new DashboardRelayMessage
        {
            Kind = DashboardRelayKind.Voicemail,
            TenantId = tenantId, CampaignId = campaignId,
            VoicemailId = voicemailId, CallRecordId = callRecordId, CallerId = callerId,
            DurationSeconds = durationSeconds, CreatedAt = createdAt,
        });

    public Task NotifyScheduledCallbackChangedAsync(
        Guid tenantId, Guid campaignId, string change, CancellationToken ct = default) =>
        PublishAsync(new DashboardRelayMessage
        {
            Kind = DashboardRelayKind.ScheduledCallback,
            TenantId = tenantId, CampaignId = campaignId, Change = change,
        });
}
