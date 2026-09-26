using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class PaymentTransactionRepository : IPaymentTransactionRepository
{
    private TenantDbContext? _ctx;
    private readonly ScopedTenantDbContextFactory _factory;

    public PaymentTransactionRepository(ScopedTenantDbContextFactory factory)
        => _factory = factory;

    private TenantDbContext Ctx => _ctx ??= _factory.Create();

    public Task<PaymentTransaction?> GetMostRecentApprovedAsync(Guid callRecordId, CancellationToken ct = default)
        => Ctx.PaymentTransactions
            .Where(t => t.CallRecordId == callRecordId
                && t.Status == PaymentTransactionStatus.Approved
                && t.VoidedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(PaymentTransaction transaction, CancellationToken ct = default)
        => await Ctx.PaymentTransactions.AddAsync(transaction, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => Ctx.SaveChangesAsync(ct);
}
