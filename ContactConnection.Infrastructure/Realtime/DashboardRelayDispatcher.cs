using System.Text.Json;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.Realtime;

/// <summary>
/// Inverse of <see cref="RedisPublishingDashboardNotifier"/>: turns a relayed
/// <see cref="DashboardRelayMessage"/> back into the matching <see cref="IDashboardNotifier"/>
/// call. Used by the API's <c>DashboardRelaySubscriber</c>; kept as a pure static so it can be
/// unit-tested without a running host.
/// </summary>
public static class DashboardRelayDispatcher
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static DashboardRelayMessage? TryParse(string json)
    {
        try { return JsonSerializer.Deserialize<DashboardRelayMessage>(json, JsonOpts); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Dispatch one message to <paramref name="notifier"/>. Returns false for an unrecognized
    /// <see cref="DashboardRelayMessage.Kind"/> (forward-compat: a newer publisher may emit a kind
    /// this build doesn't handle yet).
    /// </summary>
    public static async Task<bool> DispatchAsync(
        DashboardRelayMessage message, IDashboardNotifier notifier, CancellationToken ct = default)
    {
        switch (message.Kind)
        {
            case DashboardRelayKind.AgentState:
                await notifier.NotifyAgentStateChangedAsync(
                    message.TenantId, message.AgentId ?? Guid.Empty,
                    message.StateCode ?? "", message.Label ?? "",
                    message.Since ?? DateTimeOffset.UtcNow, ct);
                return true;

            case DashboardRelayKind.CallState:
                await notifier.NotifyCallStateChangedAsync(
                    message.TenantId, message.CampaignId ?? Guid.Empty, message.State ?? "", ct);
                return true;

            case DashboardRelayKind.AgentRegistration:
                await notifier.NotifyAgentRegistrationChangedAsync(
                    message.TenantId, message.AgentId ?? Guid.Empty,
                    message.Registered ?? false, message.Since, ct);
                return true;

            case DashboardRelayKind.Voicemail:
                await notifier.NotifyVoicemailReceivedAsync(
                    message.TenantId, message.CampaignId ?? Guid.Empty,
                    message.VoicemailId ?? Guid.Empty, message.CallRecordId ?? Guid.Empty,
                    message.CallerId, message.DurationSeconds ?? 0,
                    message.CreatedAt ?? DateTimeOffset.UtcNow, ct);
                return true;

            case DashboardRelayKind.ScheduledCallback:
                await notifier.NotifyScheduledCallbackChangedAsync(
                    message.TenantId, message.CampaignId ?? Guid.Empty, message.Change ?? "", ct);
                return true;

            default:
                return false;
        }
    }
}
