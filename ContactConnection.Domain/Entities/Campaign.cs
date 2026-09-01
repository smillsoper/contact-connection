namespace ContactConnection.Domain.Entities;

public class Campaign
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ClientId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Status { get; private set; } = CampaignStatus.Active;
    public string? Description { get; private set; }

    // The fallback CRM script flow agents run for calls in this campaign.
    public Guid? FlowId { get; private set; }

    // Telephony flow executed by ESL when an inbound call arrives on this campaign's DID.
    public Guid? InboundFlowId { get; private set; }

    // Telephony flow to execute before the agent dials (manual outbound only).
    public Guid? OutboundFlowId { get; private set; }

    // Telephony direction
    public string Direction { get; private set; } = CampaignDirection.Inbound;
    public string DialMode { get; private set; } = CampaignDialMode.Manual;  // outbound only
    public string? CallerIdNumber { get; private set; }   // outbound only

    // Campaign-level routing priority (1–10; higher = preferred when agent eligible for multiple)
    public int Priority { get; private set; } = 5;

    // How a queued call is delivered to agents — see CampaignRingStrategy.
    public string RingStrategy { get; private set; } = CampaignRingStrategy.RingAll;

    // Only meaningful when RingStrategy == RingTopNByProficiency — how many top-ranked agents to ring.
    public int RingTopN { get; private set; } = 3;

    // After-call work time agents must complete before going ready again
    public int AfterCallWorkSeconds { get; private set; } = 30;

    // Queue behaviour
    public int MaxQueueSize { get; private set; } = 50;
    public int QueueTimeoutSeconds { get; private set; } = 300;
    public int ServiceLevelThresholdSeconds { get; private set; } = 30;

    // Calls that hang up while in_queue within this many seconds are a "short" abandon;
    // beyond it, a "long" abandon. Drives abandon-length classification in call state history.
    public int ShortAbandonThresholdSeconds { get; private set; } = 10;

    // Queue acceleration: raise waiting caller's effective priority every N seconds
    public bool QueueAccelerationEnabled { get; private set; }
    public int QueueAccelerationIntervalSeconds { get; private set; } = 60;
    public int QueueAccelerationPriorityBoost { get; private set; } = 1;

    // ── Call recording policy ────────────────────────────────────────────────
    // This is the ceiling, not the mechanism: RecordingMode says what is permitted
    // on this campaign; a tf_record node in the telephony flow does the actual
    // start/stop/mask within that, and where it sits in the flow decides
    // IVR-vs-conversation coverage. See ARCHITECTURE.md §13 / §14.
    public string RecordingMode { get; private set; } = Entities.RecordingMode.Disabled;
    public string ConsentModel { get; private set; } = Entities.ConsentModel.OneParty;

    // If true and uuid_record fails to start, the call is not connected (apology + hangup)
    // rather than proceeding un-recorded. For compliance-critical campaigns.
    public bool RecordingRequired { get; private set; }

    // Record caller and agent on separate channels — near-free, and required for
    // clean downstream diarisation / selective redaction.
    public bool RecordStereo { get; private set; } = true;

    // Play a periodic audible tone while recording (jurisdiction-dependent).
    public bool RecordingBeepEnabled { get; private set; }

    // Automatically mask the recording whenever the agent places the caller on hold.
    public bool AutoMaskOnHold { get; private set; }

    // Retention window for finished recordings; drives the purge job (job itself is separate).
    public int RecordingRetentionDays { get; private set; } = 90;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation
    public Client? Client { get; private set; }

    private readonly List<PhoneNumber> _phoneNumbers = [];
    public IReadOnlyList<PhoneNumber> PhoneNumbers => _phoneNumbers.AsReadOnly();

    private readonly List<AgentCampaignAssignment> _agentAssignments = [];
    public IReadOnlyList<AgentCampaignAssignment> AgentAssignments => _agentAssignments.AsReadOnly();

    private readonly List<GroupCampaignAssignment> _groupAssignments = [];
    public IReadOnlyList<GroupCampaignAssignment> GroupAssignments => _groupAssignments.AsReadOnly();

    private readonly List<CampaignExternalNumber> _externalNumbers = [];
    public IReadOnlyList<CampaignExternalNumber> ExternalNumbers => _externalNumbers.AsReadOnly();

    private Campaign() { }

    public static Campaign Create(
        Guid tenantId,
        Guid clientId,
        string name,
        string slug,
        string? description = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Campaign
        {
            Id          = Guid.NewGuid(),
            TenantId    = tenantId,
            ClientId    = clientId,
            Name        = name.Trim(),
            Slug        = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            Status      = CampaignStatus.Active,
            CreatedAt   = now,
            UpdatedAt   = now
        };
    }

    public void Update(
        string name,
        string? description,
        string direction,
        string dialMode,
        int priority,
        int afterCallWorkSeconds,
        string? callerIdNumber,
        int maxQueueSize,
        int queueTimeoutSeconds,
        int serviceLevelThresholdSeconds,
        int shortAbandonThresholdSeconds,
        bool queueAccelerationEnabled,
        int queueAccelerationIntervalSeconds,
        int queueAccelerationPriorityBoost,
        string ringStrategy,
        int ringTopN)
    {
        Name                                 = name.Trim();
        Description                          = description?.Trim();
        Direction                            = direction == CampaignDirection.Outbound ? CampaignDirection.Outbound : CampaignDirection.Inbound;
        DialMode                             = Direction == CampaignDirection.Outbound && CampaignDialMode.IsValid(dialMode) ? dialMode : CampaignDialMode.Manual;
        Priority                             = Math.Clamp(priority, 1, 10);
        AfterCallWorkSeconds                 = Math.Max(0, afterCallWorkSeconds);
        CallerIdNumber                       = callerIdNumber?.Trim();
        MaxQueueSize                         = Math.Max(1, maxQueueSize);
        QueueTimeoutSeconds                  = Math.Max(0, queueTimeoutSeconds);
        ServiceLevelThresholdSeconds         = Math.Max(0, serviceLevelThresholdSeconds);
        ShortAbandonThresholdSeconds         = Math.Max(0, shortAbandonThresholdSeconds);
        QueueAccelerationEnabled             = queueAccelerationEnabled;
        QueueAccelerationIntervalSeconds     = Math.Max(1, queueAccelerationIntervalSeconds);
        QueueAccelerationPriorityBoost       = Math.Max(1, queueAccelerationPriorityBoost);
        RingStrategy                         = CampaignRingStrategy.IsValid(ringStrategy) ? ringStrategy : CampaignRingStrategy.RingAll;
        RingTopN                             = Math.Max(1, ringTopN);
        UpdatedAt                            = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Sets this campaign's call-recording policy. Kept separate from <see cref="Update"/>
    /// (which is already a long positional list) so recording config stays cohesive and
    /// independently testable — same reasoning as the dedicated flow-assignment methods.
    /// Invalid enum values fall back to the safe default; numeric inputs are clamped.
    /// </summary>
    public void ConfigureRecording(
        string recordingMode,
        string consentModel,
        bool recordingRequired,
        bool recordStereo,
        bool recordingBeepEnabled,
        bool autoMaskOnHold,
        int recordingRetentionDays)
    {
        RecordingMode          = Entities.RecordingMode.IsValid(recordingMode) ? recordingMode : Entities.RecordingMode.Disabled;
        ConsentModel           = Entities.ConsentModel.IsValid(consentModel) ? consentModel : Entities.ConsentModel.OneParty;
        RecordingRequired      = recordingRequired;
        RecordStereo           = recordStereo;
        RecordingBeepEnabled   = recordingBeepEnabled;
        AutoMaskOnHold         = autoMaskOnHold;
        RecordingRetentionDays = Math.Clamp(recordingRetentionDays, 1, 3650);
        UpdatedAt              = DateTimeOffset.UtcNow;
    }

    public void AssignFlow(Guid flowId)         { FlowId = flowId;         UpdatedAt = DateTimeOffset.UtcNow; }
    public void RemoveFlow()                    { FlowId = null;           UpdatedAt = DateTimeOffset.UtcNow; }
    public void AssignInboundFlow(Guid flowId)  { InboundFlowId = flowId;  UpdatedAt = DateTimeOffset.UtcNow; }
    public void RemoveInboundFlow()             { InboundFlowId = null;    UpdatedAt = DateTimeOffset.UtcNow; }
    public void AssignOutboundFlow(Guid flowId) { OutboundFlowId = flowId; UpdatedAt = DateTimeOffset.UtcNow; }
    public void RemoveOutboundFlow()            { OutboundFlowId = null;   UpdatedAt = DateTimeOffset.UtcNow; }

    public void Activate()   { Status = CampaignStatus.Active;   UpdatedAt = DateTimeOffset.UtcNow; }
    public void Pause()      { Status = CampaignStatus.Paused;   UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { Status = CampaignStatus.Inactive; UpdatedAt = DateTimeOffset.UtcNow; }
}

public static class CampaignStatus
{
    public const string Active   = "active";
    public const string Paused   = "paused";
    public const string Inactive = "inactive";
}

public static class CampaignDirection
{
    public const string Inbound  = "inbound";
    public const string Outbound = "outbound";
}

/// <summary>
/// The recording ceiling for a campaign. A tf_record node still drives the actual
/// start/stop/mask — this only bounds what it's allowed to do.
/// Disabled — no recording on this campaign; a tf_record start node is a no-op.
/// Full — recording may run from call arrival (IVR, hold, queue, bridge) to disconnect.
/// Conversation — recording may only run once the caller is bridged to an agent.
/// RecordAlwaysRetainByDisposition — always record the whole call; the end-of-call
///   disposition decides whether the file is kept, kept-short, or discarded.
/// </summary>
public static class RecordingMode
{
    public const string Disabled                        = "disabled";
    public const string Full                            = "full";
    public const string Conversation                    = "conversation";
    public const string RecordAlwaysRetainByDisposition = "record_always_retain_by_disposition";

    public static bool IsValid(string value) =>
        value is Disabled or Full or Conversation or RecordAlwaysRetainByDisposition;

    /// <summary>True when recording is permitted before the caller is bridged to an agent.</summary>
    public static bool AllowsPreBridge(string value) =>
        value is Full or RecordAlwaysRetainByDisposition;
}

/// <summary>
/// OneParty — record without an announcement (one-party-consent jurisdictions).
/// TwoPartyAnnounce — an announcement must play before recording begins/retains.
/// TwoPartyAnnounceOptout — announcement plays and the caller may decline recording
///   via DTMF (declining stops/suppresses recording or routes to a non-recorded path).
/// </summary>
public static class ConsentModel
{
    public const string OneParty               = "one_party";
    public const string TwoPartyAnnounce       = "two_party_announce";
    public const string TwoPartyAnnounceOptout = "two_party_announce_optout";

    public static bool IsValid(string value) =>
        value is OneParty or TwoPartyAnnounce or TwoPartyAnnounceOptout;

    public static bool RequiresAnnouncement(string value) =>
        value is TwoPartyAnnounce or TwoPartyAnnounceOptout;
}

public static class CampaignDialMode
{
    public const string Manual      = "manual";
    public const string Progressive = "progressive";
    public const string Predictive  = "predictive";

    public static bool IsValid(string value) =>
        value is Manual or Progressive or Predictive;
}

/// <summary>
/// How a queued call is delivered to agents on this campaign.
/// RingAll — broadcast to every eligible available agent simultaneously, first click wins.
///   The original/default behavior — good fit for a shared main tenant line.
/// AutoAnswerBestAgent — the system selects the single best eligible available agent (by
///   effective proficiency, tie-broken by longest idle) and force-delivers the call: the
///   agent's softphone auto-answers with no manual click. Best fit for most tenant-client
///   campaigns where a specific best-qualified agent should get the call, not a click-race.
/// RingTopNByProficiency — rings only the top RingTopN ranked eligible agents simultaneously,
///   first click wins among that subset. A middle ground between the two.
/// </summary>
public static class CampaignRingStrategy
{
    public const string RingAll = "ring_all";
    public const string AutoAnswerBestAgent = "auto_answer_best_agent";
    public const string RingTopNByProficiency = "ring_top_n_by_proficiency";

    public static bool IsValid(string value) =>
        value is RingAll or AutoAnswerBestAgent or RingTopNByProficiency;
}
