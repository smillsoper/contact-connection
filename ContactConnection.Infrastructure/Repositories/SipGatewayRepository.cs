using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class SipGatewayRepository : ISipGatewayRepository
{
    private readonly ContactConnectionDbContext _db;

    public SipGatewayRepository(ContactConnectionDbContext db) => _db = db;

    public Task<SipGateway?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SipGateways.FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<SipGateway?> GetByNameAsync(string name, CancellationToken ct = default) =>
        _db.SipGateways.FirstOrDefaultAsync(g => g.Name == name, ct);

    public Task<List<SipGateway>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.SipGateways.Where(g => g.TenantId == tenantId).OrderBy(g => g.Name).ToListAsync(ct);

    public async Task AddAsync(SipGateway gateway, CancellationToken ct = default) =>
        await _db.SipGateways.AddAsync(gateway, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
