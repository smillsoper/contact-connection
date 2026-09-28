using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IAgentGroupRepository
{
    Task<AgentGroup?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AgentGroup?> GetByIdWithMembersAsync(Guid id, CancellationToken ct = default);
    Task<List<AgentGroup>> GetAllAsync(CancellationToken ct = default);
    Task<AgentGroupMember?> GetMemberAsync(Guid groupId, Guid agentId, CancellationToken ct = default);
    Task AddMemberAsync(AgentGroupMember member, CancellationToken ct = default);
    Task RemoveMemberAsync(AgentGroupMember member, CancellationToken ct = default);
    Task AddAsync(AgentGroup group, CancellationToken ct = default);

    /// <summary>The group's active campaign assignments (with routing tier settings).</summary>
    Task<List<GroupCampaignAssignment>> GetActiveCampaignAssignmentsAsync(Guid groupId, CancellationToken ct = default);
    Task<List<AgentGroupMemberCampaignExclusion>> GetMemberExclusionsAsync(Guid groupId, CancellationToken ct = default);
    /// <summary>Replaces one member's exclusions with exactly <paramref name="excludedCampaignIds"/>.</summary>
    Task SetMemberExclusionsAsync(Guid groupId, Guid agentId, IReadOnlyCollection<Guid> excludedCampaignIds, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
