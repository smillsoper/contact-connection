using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface ICampaignRepository
{
    Task<Campaign?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Campaign?> GetByIdWithAssignmentsAsync(Guid id, CancellationToken ct = default);
    Task<List<Campaign>> GetAllAsync(Guid? clientId = null, CancellationToken ct = default);
    Task AddAsync(Campaign campaign, CancellationToken ct = default);

    // Agent assignments
    Task AddAgentAssignmentAsync(AgentCampaignAssignment assignment, CancellationToken ct = default);
    Task<AgentCampaignAssignment?> GetAgentAssignmentAsync(Guid campaignId, Guid agentId, CancellationToken ct = default);

    // Group assignments
    Task AddGroupAssignmentAsync(GroupCampaignAssignment assignment, CancellationToken ct = default);
    Task<GroupCampaignAssignment?> GetGroupAssignmentAsync(Guid campaignId, Guid groupId, CancellationToken ct = default);

    // External numbers (manual outbound transfer targets)
    Task<List<CampaignExternalNumber>> GetExternalNumbersAsync(Guid campaignId, CancellationToken ct = default);
    Task<CampaignExternalNumber?> GetExternalNumberByIdAsync(Guid campaignId, Guid numberId, CancellationToken ct = default);
    Task<List<CampaignExternalNumber>> GetClientTransferNumbersAsync(Guid campaignId, CancellationToken ct = default);
    Task AddExternalNumberAsync(CampaignExternalNumber number, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
