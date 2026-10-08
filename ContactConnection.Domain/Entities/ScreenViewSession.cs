namespace ContactConnection.Domain.Entities;

/// <summary>
/// Audit of a supervisor watching an agent's screen live (S183): who, whom, when it connected and ended, and how many
/// times they pointed. The agent always sees a banner while it's happening.
/// </summary>
public class ScreenViewSession
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid ViewerId { get; private set; }
    public string ViewerName { get; private set; } = "";
    public DateTimeOffset RequestedAt { get; private set; }
    /// <summary>Video started flowing (null: never connected — declined, no portal open…).</summary>
    public DateTimeOffset? ConnectedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string? EndReason { get; private set; }
    public int PointCount { get; private set; }

    private ScreenViewSession() { }

    public static ScreenViewSession Start(Guid tenantId, Guid agentId, Guid viewerId, string viewerName) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, AgentId = agentId, ViewerId = viewerId, ViewerName = viewerName,
        RequestedAt = DateTimeOffset.UtcNow,
    };

    public void Connected() => ConnectedAt ??= DateTimeOffset.UtcNow;
    public void Pointed() => PointCount++;

    public void End(string reason)
    {
        if (EndedAt is not null) return;
        EndedAt = DateTimeOffset.UtcNow;
        EndReason = reason.Length > 120 ? reason[..120] : reason;
    }
}
