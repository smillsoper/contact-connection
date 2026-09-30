namespace ContactConnection.Domain.Entities;

/// <summary>
/// One active execution of a flow against a specific call record and interaction.
/// Tracks current position in the graph, variable state, and execution history.
/// Active state lives in Redis; completed sessions are persisted here for audit/replay.
/// </summary>
public class FlowSession
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FlowId { get; private set; }
    public int FlowVersion { get; private set; }
    public Guid CallRecordId { get; private set; }
    public Guid InteractionId { get; private set; }
    public Guid AgentId { get; private set; }

    public string CurrentNodeId { get; private set; } = string.Empty;
    public string Status { get; private set; } = FlowSessionStatus.Active;

    /// <summary>
    /// Runtime variable store — all {{flow.*}}, {{input.*}}, {{api.*}} values.
    /// JSON: { "flow.offerCode": "2PACK", "input.node_001": "John", ... }
    /// </summary>
    public string VariableStore { get; private set; } = "{}";

    /// <summary>
    /// Ordered log of every node visited — for call trace view and flow relaunch.
    /// JSON array of NodeExecutionRecord objects.
    /// </summary>
    public string ExecutionHistory { get; private set; } = "[]";

    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Required by EF Core
    private FlowSession() { }

    public static FlowSession Create(
        Guid tenantId,
        Guid flowId,
        int flowVersion,
        Guid callRecordId,
        Guid interactionId,
        Guid agentId,
        string entryNodeId)
    {
        return new FlowSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FlowId = flowId,
            FlowVersion = flowVersion,
            CallRecordId = callRecordId,
            InteractionId = interactionId,
            AgentId = agentId,
            CurrentNodeId = entryNodeId,
            Status = FlowSessionStatus.Active,
            VariableStore = "{}",
            ExecutionHistory = "[]",
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void AdvanceTo(string nodeId, string variableStore, string executionHistory)
    {
        CurrentNodeId = nodeId;
        VariableStore = variableStore;
        ExecutionHistory = executionHistory;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Rewrites the stored state without moving the session — a post-call correction
    /// (edited variables, a re-run API call) on a session that may already be complete.</summary>
    public void ReplaceState(string variableStore, string executionHistory)
    {
        VariableStore = variableStore;
        ExecutionHistory = executionHistory;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>A supervisor took the call over (S167) — the script continues under their name.</summary>
    public void ReassignAgent(Guid agentId)
    {
        AgentId = agentId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(string variableStore, string executionHistory)
    {
        Status = FlowSessionStatus.Complete;
        VariableStore = variableStore;
        ExecutionHistory = executionHistory;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Abandon()
    {
        Status = FlowSessionStatus.Abandoned;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public static class FlowSessionStatus
{
    public const string Active = "active";
    public const string Complete = "complete";
    public const string Abandoned = "abandoned";
}
