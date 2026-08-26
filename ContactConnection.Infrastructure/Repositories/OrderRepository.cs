using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private TenantDbContext? _ctx;
    private readonly ScopedTenantDbContextFactory _factory;

    public OrderRepository(ScopedTenantDbContextFactory factory)
        => _factory = factory;

    private TenantDbContext Ctx => _ctx ??= _factory.Create();

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Ctx.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Order?> GetByCallRecordIdAsync(Guid callRecordId, CancellationToken ct = default)
        => Ctx.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.CallRecordId == callRecordId, ct);

    public Task<Order?> GetByLineIdAsync(Guid lineId, CancellationToken ct = default)
        => Ctx.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Lines.Any(l => l.Id == lineId), ct);

    public async Task AddAsync(Order order, CancellationToken ct = default)
        => await Ctx.Orders.AddAsync(order, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => Ctx.SaveChangesAsync(ct);
}
