using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class OfferRepository : IOfferRepository
{
    private TenantDbContext? _ctx;
    private readonly ScopedTenantDbContextFactory _factory;

    public OfferRepository(ScopedTenantDbContextFactory factory)
        => _factory = factory;

    private TenantDbContext Ctx => _ctx ??= _factory.Create();

    public Task<Offer?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Ctx.Offers
            .Include(o => o.Product)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<List<Offer>> GetByProductIdAsync(Guid productId, CancellationToken ct = default)
        => Ctx.Offers
            .Where(o => o.ProductId == productId)
            .OrderBy(o => o.Name)
            .ToListAsync(ct);

    public Task<List<Offer>> GetAvailableForContextAsync(Guid productId, Guid? clientId, Guid? campaignId, CancellationToken ct = default)
        => Ctx.Offers
            .Where(o => o.ProductId == productId
                && (o.ClientId == null || o.ClientId == clientId)
                && (o.CampaignId == null || o.CampaignId == campaignId))
            .OrderBy(o => o.Name)
            .ToListAsync(ct);

    public Task<List<Offer>> GetActiveAsync(CancellationToken ct = default)
        => Ctx.Offers
            .Include(o => o.Product)
            .Where(o => o.IsActive)
            .OrderBy(o => o.Name)
            .ToListAsync(ct);

    public async Task AddAsync(Offer offer, CancellationToken ct = default)
        => await Ctx.Offers.AddAsync(offer, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => Ctx.SaveChangesAsync(ct);
}
