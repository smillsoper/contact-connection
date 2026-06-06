using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class PortalApiEndpointRepository : IPortalApiEndpointRepository
{
    private readonly ContactConnectionDbContext _db;

    public PortalApiEndpointRepository(ContactConnectionDbContext db) => _db = db;

    public Task<List<PortalApiEndpoint>> GetByDefinitionAsync(Guid definitionId, CancellationToken ct = default) =>
        _db.PortalApiEndpoints
            .Where(e => e.DefinitionId == definitionId)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.Name)
            .ToListAsync(ct);

    public Task<List<PortalApiEndpoint>> GetBySubTypeAsync(string apiSubType, CancellationToken ct = default) =>
        _db.PortalApiEndpoints
            .Where(e => e.ApiSubType == apiSubType && e.IsActive)
            .OrderByDescending(e => e.IsPreferred)
            .ThenBy(e => e.SortOrder)
            .ToListAsync(ct);

    public Task<PortalApiEndpoint?> GetPreferredBySubTypeAsync(string apiSubType, CancellationToken ct = default) =>
        _db.PortalApiEndpoints
            .FirstOrDefaultAsync(e => e.ApiSubType == apiSubType && e.IsPreferred && e.IsActive, ct);

    public Task<PortalApiEndpoint?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PortalApiEndpoints.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task ClearPreferredForSubTypeAsync(string apiSubType, CancellationToken ct = default)
    {
        var current = await _db.PortalApiEndpoints
            .FirstOrDefaultAsync(e => e.ApiSubType == apiSubType && e.IsPreferred, ct);
        current?.ClearPreferred();
    }

    public async Task AddAsync(PortalApiEndpoint endpoint, CancellationToken ct = default) =>
        await _db.PortalApiEndpoints.AddAsync(endpoint, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);

    public Task DeleteAsync(PortalApiEndpoint endpoint, CancellationToken ct = default)
    {
        _db.PortalApiEndpoints.Remove(endpoint);
        return Task.CompletedTask;
    }
}
