namespace ContactConnection.Application.Interfaces.Services;

public interface ITelephonyFlowEngine
{
    Task ExecuteAsync(TelephonyFlowContext context, CancellationToken ct = default);

    /// <summary>
    /// Resumes flow execution from a specific node, using the live session's variable state.
    /// Used by PLAYBACK_STOP handling to continue after audio ends.
    /// </summary>
    Task ResumeFromNodeAsync(string channelUuid, string nodeId, IEslCommander esl, CancellationToken ct = default);

    /// <summary>
    /// Hands the live call to a different telephony flow: swaps the cached definition / event
    /// handlers on the session and runs the target flow from its entry node against the same
    /// parked channel. Shared flow vars carry over. Used by the tf_transfer node's
    /// "telephony_flow" destination. Returns false if the target flow is missing / not an active
    /// telephony flow / has no entry node.
    /// </summary>
    Task<bool> SwitchFlowAsync(string channelUuid, Guid targetFlowId, IEslCommander esl, CancellationToken ct = default);

    /// <summary>
    /// Fires a named lifecycle event, executing the matching event-listener branch in the
    /// telephony flow that is live for this channel. Returns the CRM flow session if a
    /// tf_script_pop node fired during the branch.
    /// </summary>
    Task<FireEventResult> FireEventAsync(
        string channelUuid,
        string eventName,
        FireEventContext eventCtx,
        CancellationToken ct = default);
}

public class TelephonyFlowContext
{
    public required string ChannelUuid { get; init; }
    public required string CallerNumber { get; init; }
    public required string DestinationNumber { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid CampaignId { get; init; }
    public required Guid CallRecordId { get; init; }
    public required string TenantSubdomain { get; init; }
    public required string TenantSchemaName { get; init; }
    /// <summary>IANA timezone for the tenant (e.g. "America/Chicago").</summary>
    public required string TenantTimezone { get; init; }
    /// <summary>ESL connection — null during event branches (bridge already established).</summary>
    public IEslCommander? Esl { get; init; }
    public Dictionary<string, string> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Session var keys a handler wants deleted from the persisted session. The engine's
    /// session-sync only ever copies <see cref="Vars"/> <em>into</em> the session, so removing a
    /// key from <see cref="Vars"/> alone is silently undone on the next sync (notably on the
    /// PLAYBACK_STOP / ivr_done resume path). A handler that needs a key gone — e.g.
    /// tf_scheduled_callback clearing <c>_queued</c> so the caller isn't still deliverable —
    /// calls <see cref="RemoveSessionVar"/>.</summary>
    public HashSet<string> VarsToRemove { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Drop a var from both the live context and the persisted session on the next sync.</summary>
    public void RemoveSessionVar(string key)
    {
        Vars.Remove(key);
        VarsToRemove.Add(key);
    }

    /// <summary>Channel variables from the CHANNEL_PARK event (e.g. SIP headers).</summary>
    public IReadOnlyDictionary<string, string> ChannelVars { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Run this specific telephony flow instead of resolving one from the phone number /
    /// campaign. Set for a fired scheduled callback (its <c>cc_target_flow_id</c>) so the answered
    /// leg lands in a designated flow rather than re-running the inbound flow.</summary>
    public Guid? FlowIdOverride { get; init; }

    // ── Dial cancellation ─────────────────────────────────────────────────────
    public bool IsCancelled { get; private set; }
    public string? CancelMessage { get; private set; }

    public void Cancel(string message)
    {
        IsCancelled   = true;
        CancelMessage = message;
    }

    // ── Event-branch fields (populated by FireEventAsync from FireEventContext) ─
    /// <summary>Agent who answered — available in agent_answer event branches.</summary>
    public Guid? AnsweringAgentId { get; set; }
    /// <summary>CallInteraction ID for the answering session.</summary>
    public Guid? InteractionId { get; set; }
    /// <summary>
    /// CRM flow engine — only available when FireEventAsync is called from an HTTP request scope.
    /// Used by tf_script_pop to start a CRM session for the answering agent.
    /// </summary>
    public IFlowEngine? FlowEngine { get; set; }

    public TelephonyFlowTrace? Trace { get; set; }
}

// ── Event firing ──────────────────────────────────────────────────────────────

public class FireEventContext
{
    public Guid? AgentId { get; set; }
    public Guid? InteractionId { get; set; }
    /// <summary>CRM flow engine — inject from HTTP request scope when available.</summary>
    public IFlowEngine? FlowEngine { get; set; }
    /// <summary>ESL connection — inject when the branch needs to issue ESL commands.</summary>
    public IEslCommander? Esl { get; set; }
    /// <summary>Extra variables injected into ctx.Vars before the branch executes.</summary>
    public Dictionary<string, string> AdditionalVars { get; set; } = new();
}

public class FireEventResult
{
    public bool Handled { get; init; }
    /// <summary>Initial CRM flow node state if tf_script_pop fired. Null otherwise.</summary>
    public FlowNodeState? CrmFlowSession { get; init; }
}

// ── Call session (Redis, keyed by channelUuid, 4-hour TTL) ───────────────────

/// <summary>
/// Represents a live telephony call. Created on CHANNEL_PARK, stored in Redis for the duration
/// of the call. EventHandlers maps lifecycle event names to the handler node IDs discovered
/// when the flow was first parsed. Vars are shared across all event branches.
/// </summary>
public class TelephonyCallSession
{
    public string ChannelUuid { get; set; } = "";
    public Guid CallRecordId { get; set; }
    public Guid TenantId { get; set; }
    public Guid CampaignId { get; set; }
    public string TenantSubdomain { get; set; } = "";
    public string TenantSchemaName { get; set; } = "";
    public string TenantTimezone { get; set; } = "";
    public string CallerNumber { get; set; } = "";
    public string DestinationNumber { get; set; } = "";
    public Guid FlowId { get; set; }
    /// <summary>Cached flow definition JSON — avoids a DB round-trip on each event fire.</summary>
    public string FlowDefinitionJson { get; set; } = "";
    public Dictionary<string, string> Vars { get; set; } = new();
    /// <summary>Maps event name → nodeId (e.g. "agent_answer" → "tf_on_agent_answer_1").</summary>
    public Dictionary<string, string> EventHandlers { get; set; } = new();
}

// ── Flow execution trace ──────────────────────────────────────────────────────

public class TelephonyFlowTrace
{
    public Guid FlowId { get; set; }
    public string FlowName { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? TerminationReason { get; set; }
    public List<TelephonyFlowTraceStep> Steps { get; set; } = [];
}

public class TelephonyFlowTraceStep
{
    public string NodeId { get; set; } = "";
    public string NodeType { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string? TransitionTaken { get; set; }
    public string? NextNodeId { get; set; }
    public string? Error { get; set; }
}
