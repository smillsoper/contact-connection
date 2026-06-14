using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class CampaignRepository : ICampaignRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public CampaignRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<Campaign?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.Campaigns
            .Include(c => c.Client)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Campaign?> GetByIdWithAssignmentsAsync(Guid id, CancellationToken ct = default) =>
        Db.Campaigns
            .Include(c => c.Client)
            .Include(c => c.PhoneNumbers)
            .Include(c => c.AgentAssignments)
            .Include(c => c.GroupAssignments).ThenInclude(g => g.Group).ThenInclude(g => g!.Members)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<Campaign>> GetAllAsync(Guid? clientId = null, CancellationToken ct = default)
    {
        var query = Db.Campaigns.Include(c => c.Client).AsQueryable();
        if (clientId.HasValue)
            query = query.Where(c => c.ClientId == clientId.Value);
        return query.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task AddAsync(Campaign campaign, CancellationToken ct = default) =>
        await Db.Campaigns.AddAsync(campaign, ct);

    public async Task AddAgentAssignmentAsync(AgentCampaignAssignment assignment, CancellationToken ct = default) =>
        await Db.AgentCampaignAssignments.AddAsync(assignment, ct);

    public Task<AgentCampaignAssignment?> GetAgentAssignmentAsync(Guid campaignId, Guid agentId, CancellationToken ct = default) =>
        Db.AgentCampaignAssignments.FirstOrDefaultAsync(a => a.CampaignId == campaignId && a.AgentId == agentId, ct);

    public async Task AddGroupAssignmentAsync(GroupCampaignAssignment assignment, CancellationToken ct = default) =>
        await Db.GroupCampaignAssignments.AddAsync(assignment, ct);

    public Task<GroupCampaignAssignment?> GetGroupAssignmentAsync(Guid campaignId, Guid groupId, CancellationToken ct = default) =>
        Db.GroupCampaignAssignments.FirstOrDefaultAsync(a => a.CampaignId == campaignId && a.GroupId == groupId, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
