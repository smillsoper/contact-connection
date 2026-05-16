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
}

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
}

/// <summary>
/// The state of a node after execution — sent to the agent UI via SignalR.
/// The UI is a thin renderer: it displays what the engine says to display.
/// </summary>
public class FlowNodeState
{
    public required Guid SessionId { get; init; }
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
    /// Pre-filled address form values loaded from an existing flow variable.
    /// Present when the output variable already holds address data when the node first displays.
    /// Keys match AddressSubmission property names (firstName, lastName, address1, etc.).
    /// </summary>
    public Dictionary<string, string>? PrefilledAddress { get; set; }
}

public class FlowOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
}
