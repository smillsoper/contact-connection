namespace ContactConnection.Infrastructure.Realtime;

/// <summary>
/// Wire format for the Worker → API supervisor-dashboard relay. The Worker host has no SignalR
/// hub, so a dashboard-relevant change it makes (a scheduled-callback attempt/expiry, a
/// callback abandon) can't reach a supervisor's browser directly. Instead the Worker publishes
/// one of these to <see cref="RedisChannel"/> and <c>DashboardRelaySubscriber</c> (an API hosted
/// service) picks it up and re-emits it through the real <c>IDashboardNotifier</c> / SignalR.
///
/// One flat envelope for every <c>IDashboardNotifier</c> method — <see cref="Kind"/> is the
/// discriminator and only the fields that method needs are populated.
/// </summary>
public sealed record DashboardRelayMessage
{
    /// <summary>Redis pub/sub channel the relay runs on.</summary>
    public const string RedisChannel = "contactconnection:dashboard-relay";

    public required string Kind { get; init; }
    public Guid TenantId { get; init; }

    public Guid? AgentId { get; init; }
    public Guid? CampaignId { get; init; }
    public string? StateCode { get; init; }
    public string? Label { get; init; }
    public string? State { get; init; }
    public string? Change { get; init; }
    public bool? Registered { get; init; }
    public DateTimeOffset? Since { get; init; }

    public Guid? VoicemailId { get; init; }
    public Guid? CallRecordId { get; init; }
    public string? CallerId { get; init; }
    public int? DurationSeconds { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary><see cref="DashboardRelayMessage.Kind"/> values — one per <c>IDashboardNotifier</c> method.</summary>
public static class DashboardRelayKind
{
    public const string AgentState         = "agent_state";
    public const string CallState          = "call_state";
    public const string AgentRegistration  = "agent_registration";
    public const string Voicemail          = "voicemail";
    public const string ScheduledCallback  = "scheduled_callback";
}
