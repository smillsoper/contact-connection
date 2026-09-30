namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// The server-side flow interpreter. Manages session lifecycle, node dispatch,
/// variable resolution, and SignalR push. All agent interaction goes through here.
/// </summary>
public interface IFlowEngine
{
    /// <summary>
    /// Starts a new flow session against an existing call record and interaction.
    /// Returns the initial node state to push to the agent UI.
    /// </summary>
    Task<FlowNodeState> StartAsync(StartFlowRequest request, CancellationToken ct = default);

    /// <summary>
    /// Advances the session to the next node based on agent input.
    /// Returns the next node state, or a terminal state if the flow has ended.
    /// </summary>
    Task<FlowNodeState> AdvanceAsync(AdvanceFlowRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns the current node state for an active session (e.g. on reconnect).
    /// </summary>
    Task<FlowNodeState?> GetCurrentStateAsync(Guid sessionId, CancellationToken ct = default);

    // ── Post-call review (Call Records admin) ───────────────────────────────

    /// <summary>A session's variables and its API-call nodes' last results, for the call detail
    /// view. Null when the session doesn't exist.</summary>
    Task<FlowSessionSnapshot?> GetSessionSnapshotAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Sets (or, for a null value, removes) flow variables on a session — live or
    /// finished — so a correction is what a re-run API call sends. Returns each changed key's
    /// previous value (null = wasn't set), for the audit trail.</summary>
    Task<IReadOnlyDictionary<string, string?>> UpdateSessionVariablesAsync(
        Guid sessionId, IReadOnlyDictionary<string, string?> changes, CancellationToken ct = default);

    /// <summary>Runs one of the session's api_call or authorize_payment nodes again, exactly as the
    /// flow would, against the call's current data:
    ///   api_call — same endpoint, template, output variable and oncePerCall guard (an order that
    ///     already posted replays instead of posting twice);
    ///   authorize_payment — re-authorizes the (possibly corrected) cart total from the card still on
    ///     file: no-op if unchanged, else void + re-authorize (needs the campaign's card retention to
    ///     have kept the card — CardDataRetentionMode.UntilOrderSubmitted).
    /// The flow does not move on; only the node's output variables change.</summary>
    Task<ApiCallRerunResult> RerunNodeAsync(Guid sessionId, string nodeId, CancellationToken ct = default);

    /// <summary>If an agent still has this session open: reload its call data from the call record
    /// (so {{caller.*}} / {{call_record.*}} show a correction made elsewhere) and push the refreshed
    /// current node to the agent with <paramref name="message"/>. False when the session isn't live.</summary>
    Task<bool> PushLiveUpdateAsync(Guid sessionId, string message, CancellationToken ct = default);

    /// <summary>Closes a session out (Call Records "Finalize"): an open script shows the agent its
    /// finished checkmark with <paramref name="message"/> and closes, exactly like reaching an end
    /// node; an orphaned one (agent's screen long gone) is just marked complete. Card data follows
    /// the campaign's retention rule, as on any script end. False if it was already finished.</summary>
    Task<bool> FinalizeSessionAsync(Guid sessionId, string message, CancellationToken ct = default);

    /// <summary>The CRM scripts these agents have open right now (live in Redis), whether or not
    /// they're on a phone call — the supervisor Agent List links each to its call's review page.</summary>
    Task<IReadOnlyList<LiveFlowSession>> GetLiveSessionsForAgentsAsync(
        IReadOnlyCollection<Guid> agentIds, CancellationToken ct = default);
}

public record LiveFlowSession(Guid AgentId, Guid SessionId, Guid CallRecordId, string? FlowName, DateTimeOffset StartedAt);

public class FlowSessionSnapshot
{
    public required Guid SessionId { get; init; }
    /// <summary>True while an agent still has the script open (its state is live in Redis).</summary>
    public bool IsLive { get; init; }
    public Dictionary<string, string> FlowVars { get; init; } = [];
    public Dictionary<string, string> Inputs { get; init; } = [];
    public string? CommitLabel { get; init; }
    public List<ApiCallNodeSummary> ApiCalls { get; init; } = [];
}

/// <summary>One api_call or authorize_payment node reachable from the session's flow (sub-flows
/// included).</summary>
public class ApiCallNodeSummary
{
    public required string NodeId { get; init; }
    /// <summary>"api_call" or "authorize_payment".</summary>
    public string NodeType { get; init; } = "api_call";
    /// <summary>api_call marked as the order submission — wipes the captured card on success.</summary>
    public bool ReleasesCardData { get; init; }
    public required string Label { get; init; }
    public string? OutputVariable { get; init; }
    public bool OncePerCall { get; init; }
    /// <summary>Times this node ran on the call (execution history).</summary>
    public int RunCount { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    /// <summary>"true"/"false" from {output}.success (authorize_payment: .succeeded), or null if it
    /// never ran / has no output variable.</summary>
    public string? Success { get; init; }
    public string? StatusCode { get; init; }
    public string? Error { get; init; }
    public string? Response { get; init; }
}

public record ApiCallRerunResult(
    bool Success, string Transition, string? StatusCode, string? Error, string? Response, bool Replayed,
    string NodeType = "api_call");

public class StartFlowRequest
{
    public required Guid FlowId { get; init; }
    public required Guid CallRecordId { get; init; }
    public required Guid InteractionId { get; init; }
    public required Guid AgentId { get; init; }
    public required Guid TenantId { get; init; }
}

public class AdvanceFlowRequest
{
    public required Guid SessionId { get; init; }

    /// <summary>
    /// Agent's input for the current node (text entered, option selected, etc.).
    /// Null for nodes that don't capture input (script nodes advanced by button click).
    /// </summary>
    public string? InputValue { get; init; }

    /// <summary>
    /// For branch nodes: which transition the agent chose (or the engine evaluated).
    /// For most nodes this is "default".
    /// </summary>
    public string Transition { get; init; } = "default";

    /// <summary>
    /// When set, the engine jumps to this section node instead of advancing the current node.
    /// The engine auto-advances through the section node and stops at the first input node.
    /// </summary>
    public string? JumpToSectionNodeId { get; init; }
}

/// <summary>
/// A section node visible in the agent's jump dropdown.
/// </summary>
public class JumpTarget
{
    public required string SectionNodeId { get; init; }
    public required string Name { get; init; }
    public bool IsCurrentSection { get; init; }
    public bool ClearPreviousValues { get; init; }
    public bool IsLocked { get; init; }
}

/// <summary>
/// The state of a node after execution — sent to the agent UI via SignalR.
/// The UI is a thin renderer: it displays what the engine says to display.
/// </summary>
public class FlowNodeState
{
    public required Guid SessionId { get; init; }

    /// <summary>The call this session belongs to — lets the agent UI associate each open flow tab
    /// with its own call-scoped state (e.g. the cart) instead of relying on a single global "current
    /// call" value that doesn't actually track which tab is active.</summary>
    public required Guid CallRecordId { get; init; }
    public required string NodeId { get; init; }
    public required string NodeType { get; init; }   // script | input | branch | end | ...
    public required string Label { get; init; }

    /// <summary>Resolved content for display (script text with tags substituted).</summary>
    public string? Content { get; init; }

    /// <summary>For input nodes: the type of input expected.</summary>
    public string? InputType { get; init; }          // text | select | checkbox | date | address | phone

    /// <summary>For select/radio input nodes: the available options.</summary>
    public List<FlowOption>? Options { get; init; }

    /// <summary>For branch nodes: the condition text (informational for supervisor view).</summary>
    public string? Condition { get; init; }

    /// <summary>Name of the CRM flow this session belongs to — used by the agent UI to label the tab.</summary>
    public string? FlowName { get; set; }

    /// <summary>True when the flow has reached an end node.</summary>
    public bool IsTerminal { get; init; }

    /// <summary>Locked fields from commitment events — UI renders these as locked.</summary>
    public List<string> LockedFields { get; init; } = [];

    /// <summary>Whether the field is required (email and input nodes).</summary>
    public bool Required { get; init; }

    /// <summary>Validation error to display inline — set when the node re-displays after a failed check.</summary>
    public string? ValidationError { get; set; }

    /// <summary>Label for the node's own inline script (shown above the script content).</summary>
    public string? NodeScriptLabel { get; set; }

    /// <summary>Resolved rich-text script content embedded directly on an input/email node.</summary>
    public string? NodeScriptContent { get; set; }

    /// <summary>
    /// Resolved content from the script node immediately preceding this node (if any).
    /// The UI displays this above the current node so agent reads the script and fills the input together.
    /// </summary>
    public string? ScriptContext { get; set; }

    /// <summary>For text input nodes: minimum character count (null = no minimum).</summary>
    public int? MinChars { get; set; }

    /// <summary>For text input nodes: maximum character count (null = no maximum).</summary>
    public int? MaxChars { get; set; }

    /// <summary>
    /// For text input nodes: WinForms-style mask pattern (e.g. "(000) 000-0000").
    /// Null = no mask. When set, MinChars/MaxChars are implied by the mask and should be ignored.
    /// </summary>
    public string? InputMask { get; set; }

    // ── Address node ───────────────────────────────────────────────────────

    /// <summary>When true the frontend should call the validate-address endpoint before advancing.</summary>
    public bool UseValidation { get; set; } = true;

    /// <summary>When true the Country field is shown and international postal formats are accepted.</summary>
    public bool AllowInternational { get; set; }

    /// <summary>When true the Middle Initial field is shown in the address form.</summary>
    public bool ShowMiddleInitial { get; set; }

    /// <summary>When true the Company Name field is shown in the address form.</summary>
    public bool ShowCompany { get; set; }

    /// <summary>Field names that are required (e.g. "firstName", "lastName", "address1", "zip", "city", "state").</summary>
    public List<string> RequiredFields { get; set; } = [];

    /// <summary>Per-field resolved script HTML, keyed by field name (e.g. "zip", "firstName"). Displayed when that field has focus.</summary>
    public Dictionary<string, string> FieldScripts { get; set; } = [];

    /// <summary>
    /// Pre-filled value for input/email/phone nodes — present when the output variable already
    /// holds a value from an earlier point in the flow (e.g. set by set_variable).
    /// For email/phone this is the raw string (email address or display-formatted phone number).
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Pre-filled address form values loaded from an existing flow variable.
    /// Present when the output variable already holds address data when the node first displays.
    /// Keys match AddressSubmission property names (firstName, lastName, address1, etc.).
    /// </summary>
    public Dictionary<string, string>? PrefilledAddress { get; set; }

    // ── Section state ──────────────────────────────────────────────────────

    /// <summary>Name of the section the agent is currently in (null if before first section).</summary>
    public string? CurrentSectionName { get; set; }

    /// <summary>True when the current section is locked — UI renders input nodes read-only.</summary>
    public bool SectionLocked { get; set; }

    /// <summary>Sections visible in the jump dropdown for this node.</summary>
    public List<JumpTarget>? JumpTargets { get; set; }

    /// <summary>Set once the flow has passed a commit point — the agent UI shows it as a banner;
    /// jumps back are refused by the engine (only the commit point's allowed sections remain).</summary>
    public string? CommitLabel { get; set; }

    /// <summary>
    /// When set, names a trigger_telephony_event eventName this node is waiting on — the agent UI
    /// disables manual advance and auto-advances the instant that event's telephony branch reaches
    /// its own tf_end (receiveTelephonyEventEnded), instead of relying on the agent to click
    /// Continue at the right moment. Closes the trigger_telephony_event fire-and-continue race —
    /// see project_shared_call_variables memory. Any node type may set it; only meaningful on a
    /// node with a manual "Continue" advance (typically script).
    /// </summary>
    public string? WaitForTelephonyEventName { get; init; }

    /// <summary>
    /// How long the agent UI waits for the matching receiveTelephonyEventEnded push before
    /// re-enabling manual Continue as a fallback (a missed/dropped SignalR push must not strand
    /// the agent). Null means the UI's own default (60s) applies. Per-node so a flow author can
    /// tune it for events that legitimately take longer (e.g. an older caller keying in digits
    /// slowly) without affecting other waits.
    /// </summary>
    public int? WaitForTelephonyEventTimeoutSeconds { get; init; }
}

public class FlowOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
}
