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
    Task SaveChangesAsync(CancellationToken ct = default);
}
