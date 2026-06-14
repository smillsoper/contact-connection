using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class AgentGroupRepository : IAgentGroupRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public AgentGroupRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<AgentGroup?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.AgentGroups.FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<AgentGroup?> GetByIdWithMembersAsync(Guid id, CancellationToken ct = default) =>
        Db.AgentGroups
            .Include(g => g.Members)
            .Include(g => g.CampaignAssignments)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<List<AgentGroup>> GetAllAsync(CancellationToken ct = default) =>
        Db.AgentGroups.OrderBy(g => g.Name).ToListAsync(ct);

    public Task<AgentGroupMember?> GetMemberAsync(Guid groupId, Guid agentId, CancellationToken ct = default) =>
        Db.AgentGroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.AgentId == agentId, ct);

    public async Task AddMemberAsync(AgentGroupMember member, CancellationToken ct = default) =>
        await Db.AgentGroupMembers.AddAsync(member, ct);

    public Task RemoveMemberAsync(AgentGroupMember member, CancellationToken ct = default)
    {
        Db.AgentGroupMembers.Remove(member);
        return Task.CompletedTask;
    }

    public async Task AddAsync(AgentGroup group, CancellationToken ct = default) =>
        await Db.AgentGroups.AddAsync(group, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
