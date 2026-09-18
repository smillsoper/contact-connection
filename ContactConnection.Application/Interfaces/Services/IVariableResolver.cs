namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Resolves {{namespace.field}} template tags against a runtime execution context.
///
/// Supported namespaces:
///   {{call_record.*}}   — call record fields (caller_id, first_name, email, etc.)
///   {{caller.*}}        — inbound telephony data (ani, dnis, etc.)
///   {{agent.*}}         — current agent context (id, name, role)
///   {{tenant.*}}        — tenant config values
///   {{input.[node_id]}} — value captured at a specific input node
///   {{api.[node_id].*}} — value from a specific api_call node response
///   {{flow.*}}          — variables set during flow execution via set_variable nodes
///   {{shared.*}}        — variables shared with the telephony call flow for the same call
///                         (ISharedCallVariableStore, keyed by CallRecordId) — distinct from
///                         flow.*, which stays private to this CRM session
/// </summary>
public interface IVariableResolver
{
    /// <summary>
    /// Resolves all {{...}} tags in the template string. Unknown tags are left as-is.
    /// </summary>
    string Resolve(string template, VariableContext context);

    /// <summary>
    /// Extracts all {{...}} tag references from a template without resolving them.
    /// Used for validation and dependency analysis.
    /// </summary>
    IEnumerable<string> ExtractReferences(string template);

    /// <summary>
    /// Evaluates a simple condition expression against the context.
    /// Supported operators: == != > < >= <= contains
    /// Example: "{{input.call_type}} == \"Order\""
    /// </summary>
    bool EvaluateCondition(string condition, VariableContext context);
}

/// <summary>
/// All data available to the variable resolver during flow execution.
/// Passed into every node handler and resolver call.
/// </summary>
public class VariableContext
{
    // Call record relational fields — populated at session start
    public Dictionary<string, string> CallRecord { get; init; } = [];

    // Inbound telephony data (ANI, DNIS) — populated from FreeSWITCH or screen pop
    public Dictionary<string, string> Caller { get; init; } = [];

    // Current agent
    public Dictionary<string, string> Agent { get; init; } = [];

    // Tenant config values
    public Dictionary<string, string> Tenant { get; init; } = [];

    // Values captured at input nodes — keyed by node_id
    public Dictionary<string, string> Inputs { get; init; } = [];

    // Values from api_call node responses — keyed by "node_id.field"
    public Dictionary<string, string> ApiResults { get; init; } = [];

    // Variables set via set_variable nodes — keyed by variable name
    public Dictionary<string, string> FlowVars { get; init; } = [];

    // {{shared.*}} — call-wide variables visible to both this CRM session and the telephony call
    // flow for the same call. Fetched fresh from ISharedCallVariableStore at the start of each
    // StartAsync/AdvanceAsync/GetCurrentStateAsync call (not cached across requests like the
    // dictionaries above), so a value the telephony side just set is visible as soon as the agent
    // next interacts with the script.
    public Dictionary<string, string> SharedVars { get; init; } = [];
}
