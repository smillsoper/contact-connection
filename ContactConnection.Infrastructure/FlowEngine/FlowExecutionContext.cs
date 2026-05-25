using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Infrastructure.FlowEngine;

/// <summary>
/// Runtime state for one active flow session. Loaded from Redis on each request,
/// serialized back to Redis after each advance. Written to PostgreSQL on completion.
/// </summary>
public class FlowExecutionContext
{
    public Guid SessionId { get; init; }
    public Guid FlowId { get; init; }
    public int FlowVersion { get; init; }
    public Guid CallRecordId { get; init; }
    public Guid InteractionId { get; init; }
    public Guid AgentId { get; init; }
    public Guid TenantId { get; init; }
    public string CurrentNodeId { get; set; } = string.Empty;

    // The full flow definition JSON — mutable to support execute_flow / transition_to_flow
    public JsonObject FlowDefinition { get; set; } = [];

    // Variable state — projected into VariableContext for resolver calls
    public Dictionary<string, string> FlowVars { get; init; } = [];
    public Dictionary<string, string> Inputs { get; init; } = [];
    public Dictionary<string, string> ApiResults { get; init; } = [];

    // Sub-flow call stack — pushed by execute_flow, popped when sub-flow terminates
    public List<FlowCallFrame> CallStack { get; init; } = [];

    // Section state — updated by SectionNodeHandler as execution passes through section nodes
    public string? CurrentSectionNodeId { get; set; }
    public string? CurrentSectionName { get; set; }
    public bool CurrentSectionLocked { get; set; }
    public HashSet<string> CompletedSectionNodeIds { get; init; } = [];
    // Every section node that was entered during this session — used to keep past
    // (even unfinished) sections visible in the jump dropdown.
    public HashSet<string> EncounteredSectionNodeIds { get; init; } = [];

    // Baseline data populated from call record and agent at session start
    public Dictionary<string, string> CallRecord { get; init; } = [];
    public Dictionary<string, string> Caller { get; init; } = [];
    public Dictionary<string, string> Agent { get; init; } = [];
    public Dictionary<string, string> Tenant { get; init; } = [];

    // Append-only execution log — every node visited with timestamp and input
    public List<NodeExecutionRecord> ExecutionHistory { get; init; } = [];

    // Locked fields from commitment events — enforced at engine and API layers
    public HashSet<string> LockedFields { get; init; } = [];

    // Serialization helpers ─────────────────────────────────────────────────

    public VariableContext ToVariableContext() => new()
    {
        CallRecord = CallRecord,
        Caller     = Caller,
        Agent      = Agent,
        Tenant     = Tenant,
        Inputs     = Inputs,
        ApiResults = ApiResults,
        FlowVars   = FlowVars
    };

    public string SerializeVariableStore() =>
        JsonSerializer.Serialize(new
        {
            FlowVars, Inputs, ApiResults,
            CurrentSectionNodeId, CurrentSectionName, CurrentSectionLocked,
            CompletedSectionNodeIds,
            EncounteredSectionNodeIds,
            CallStack,
        });

    public string SerializeExecutionHistory() =>
        JsonSerializer.Serialize(ExecutionHistory);

    public static FlowExecutionContext Deserialize(
        Guid sessionId, Guid flowId, int flowVersion,
        Guid callRecordId, Guid interactionId, Guid agentId, Guid tenantId,
        string currentNodeId, string definitionJson,
        string variableStoreJson, string executionHistoryJson,
        Dictionary<string, string> callRecord,
        Dictionary<string, string> caller,
        Dictionary<string, string> agent,
        Dictionary<string, string> tenant)
    {
        var varStore = JsonSerializer.Deserialize<VariableStore>(variableStoreJson)
            ?? new VariableStore();
        var history  = JsonSerializer.Deserialize<List<NodeExecutionRecord>>(executionHistoryJson)
            ?? [];
        var definition = JsonNode.Parse(definitionJson)?.AsObject() ?? [];

        return new FlowExecutionContext
        {
            SessionId               = sessionId,
            FlowId                  = flowId,
            FlowVersion             = flowVersion,
            CallRecordId            = callRecordId,
            InteractionId           = interactionId,
            AgentId                 = agentId,
            TenantId                = tenantId,
            CurrentNodeId           = currentNodeId,
            FlowDefinition          = definition,
            FlowVars                = varStore.FlowVars,
            Inputs                  = varStore.Inputs,
            ApiResults              = varStore.ApiResults,
            CurrentSectionNodeId    = varStore.CurrentSectionNodeId,
            CurrentSectionName      = varStore.CurrentSectionName,
            CurrentSectionLocked    = varStore.CurrentSectionLocked,
            CompletedSectionNodeIds   = varStore.CompletedSectionNodeIds,
            EncounteredSectionNodeIds = varStore.EncounteredSectionNodeIds,
            CallStack                 = varStore.CallStack,
            CallRecord              = callRecord,
            Caller                  = caller,
            Agent                   = agent,
            Tenant                  = tenant,
            ExecutionHistory        = history
        };
    }

    private record VariableStore(
        Dictionary<string, string> FlowVars,
        Dictionary<string, string> Inputs,
        Dictionary<string, string> ApiResults,
        string? CurrentSectionNodeId,
        string? CurrentSectionName,
        bool CurrentSectionLocked,
        HashSet<string> CompletedSectionNodeIds,
        HashSet<string> EncounteredSectionNodeIds,
        List<FlowCallFrame> CallStack)
    {
        public VariableStore() : this([], [], [], null, null, false, [], [], []) { }
    }
}

/// <summary>
/// One entry in the execution history — the immutable audit trail of node traversal.
/// </summary>
public record NodeExecutionRecord(
    string NodeId,
    string NodeType,
    string Label,
    DateTimeOffset EnteredAt,
    string? InputValue,
    string? TransitionTaken);

/// <summary>
/// Saved parent-flow state pushed onto the call stack when an execute_flow node runs.
/// Popped and restored when the sub-flow reaches its end node.
/// </summary>
public record FlowCallFrame(
    Guid FlowId,
    string DefinitionJson,
    string ReturnNodeId);
