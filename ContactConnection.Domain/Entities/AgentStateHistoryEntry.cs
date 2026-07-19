namespace ContactConnection.Domain.Entities;

/// <summary>
/// One agent state transition (available, on_call, acw, etc). Append-only; never mutated.
/// Duration of a given row = next row's EnteredAt minus this row's (or now() for the latest row).
/// </summary>
public class AgentStateHistoryEntry
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentId { get; private set; }
    public string StateCode { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public Guid? CustomCodeId { get; private set; }
    public DateTimeOffset EnteredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AgentStateHistoryEntry() { }

    public static AgentStateHistoryEntry Create(
        Guid tenantId,
        Guid agentId,
        string stateCode,
        string label,
        Guid? customCodeId,
        DateTimeOffset enteredAt)
    {
        return new AgentStateHistoryEntry
        {
            Id           = Guid.NewGuid(),
            TenantId     = tenantId,
            AgentId      = agentId,
            StateCode    = stateCode,
            Label        = label,
            CustomCodeId = customCodeId,
            EnteredAt    = enteredAt,
            CreatedAt    = DateTimeOffset.UtcNow,
        };
    }
}
