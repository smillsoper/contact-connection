using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IFlowSessionRepository
{
    Task<FlowSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FlowSession?> GetActiveByCallRecordAsync(Guid callRecordId, CancellationToken ct = default);
    /// <summary>Every session run on a call (a transfer or a relaunch can add more), oldest first.</summary>
    Task<IReadOnlyList<FlowSession>> GetByCallRecordAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>Most recent sessions, newest first — of one flow, or of any flow when flowId is null
    /// (S169: test data for the API Call node's request preview).</summary>
    Task<IReadOnlyList<FlowSession>> GetRecentAsync(Guid? flowId, int limit, CancellationToken ct = default);
    /// <summary>Sessions still marked active for these agents that moved since
    /// <paramref name="updatedSince"/> — candidates for "live" (the caller confirms each against
    /// Redis; a script closed without finishing stays "active" in Postgres).</summary>
    Task<IReadOnlyList<FlowSession>> GetActiveForAgentsAsync(
        IReadOnlyCollection<Guid> agentIds, DateTimeOffset updatedSince, CancellationToken ct = default);

    Task AddAsync(FlowSession session, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
