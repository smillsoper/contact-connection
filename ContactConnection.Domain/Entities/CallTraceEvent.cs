namespace ContactConnection.Domain.Entities;

/// <summary>
/// One node-execution step in a call's end-to-end trace — spans both the telephony
/// pre-answer engine and the CRM agent-facing engine, unified by CallRecordId.
/// Append-only; never mutated after creation.
/// </summary>
public class CallTraceEvent
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CallRecordId { get; private set; }
    public int Sequence { get; private set; }
    public string Engine { get; private set; } = TraceEngine.Telephony;
    public string NodeId { get; private set; } = string.Empty;
    public string NodeType { get; private set; } = string.Empty;
    public string? Label { get; private set; }
    public DateTimeOffset EnteredAt { get; private set; }
    public string? Detail { get; private set; }
    public string? TransitionTaken { get; private set; }
    public string? ExitReason { get; private set; }
    public string? NextNodeId { get; private set; }

    /// <summary>
    /// JSON snapshot of the call's variable/context state at the end of this step (flow
    /// variables, SIP headers, section state, etc.) — sensitive values are redacted before
    /// this is built, not at read time. Null is valid (e.g. rows recorded before this existed).
    /// </summary>
    public string? StateSnapshot { get; private set; }

    // Denormalized onto the first row for a call — lets historical search avoid a CallRecord join
    public Guid? CampaignId { get; private set; }
    public Guid? FlowId { get; private set; }
    public string? Dnis { get; private set; }
    public string? Ani { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private CallTraceEvent() { }

    public static CallTraceEvent Create(
        Guid tenantId,
        Guid callRecordId,
        int sequence,
        string engine,
        string nodeId,
        string nodeType,
        string? label,
        string? detail,
        string? transitionTaken,
        string? nextNodeId,
        string? exitReason,
        Guid? campaignId,
        Guid? flowId,
        string? dnis,
        string? ani,
        string? stateSnapshot)
    {
        return new CallTraceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CallRecordId = callRecordId,
            Sequence = sequence,
            Engine = engine,
            NodeId = nodeId,
            NodeType = nodeType,
            Label = label,
            EnteredAt = DateTimeOffset.UtcNow,
            Detail = detail,
            TransitionTaken = transitionTaken,
            NextNodeId = nextNodeId,
            ExitReason = exitReason,
            CampaignId = campaignId,
            FlowId = flowId,
            Dnis = dnis,
            Ani = ani,
            StateSnapshot = stateSnapshot,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}

public static class TraceEngine
{
    public const string Telephony = "telephony";
    public const string Crm = "crm";
}
