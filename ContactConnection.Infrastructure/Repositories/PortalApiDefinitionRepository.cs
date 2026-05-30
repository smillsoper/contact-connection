using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class PortalApiDefinitionRepository : IPortalApiDefinitionRepository
{
    private readonly ContactConnectionDbContext _db;

    public PortalApiDefinitionRepository(ContactConnectionDbContext db) => _db = db;

    public Task<List<PortalApiDefinition>> GetAllAsync(CancellationToken ct = default) =>
        _db.PortalApiDefinitions.OrderBy(d => d.ApiType).ThenBy(d => d.Name).ToListAsync(ct);

    public Task<List<PortalApiDefinition>> GetByTypeAsync(string apiType, CancellationToken ct = default) =>
        _db.PortalApiDefinitions.Where(d => d.ApiType == apiType).OrderBy(d => d.Name).ToListAsync(ct);

    public Task<PortalApiDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PortalApiDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task AddAsync(PortalApiDefinition definition, CancellationToken ct = default) =>
        await _db.PortalApiDefinitions.AddAsync(definition, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);

    public Task DeleteAsync(PortalApiDefinition definition, CancellationToken ct = default)
    {
        _db.PortalApiDefinitions.Remove(definition);
        return Task.CompletedTask;
    }
}
