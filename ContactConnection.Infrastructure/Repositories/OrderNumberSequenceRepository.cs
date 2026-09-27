using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class OrderNumberSequenceRepository : IOrderNumberSequenceRepository
{
    private TenantDbContext? _ctx;
    private readonly ScopedTenantDbContextFactory _factory;

    public OrderNumberSequenceRepository(ScopedTenantDbContextFactory factory)
        => _factory = factory;

    private TenantDbContext Ctx => _ctx ??= _factory.Create();

    public Task<OrderNumberSequence?> GetByClientIdAsync(Guid clientId, CancellationToken ct = default)
        => Ctx.OrderNumberSequences.FirstOrDefaultAsync(s => s.ClientId == clientId, ct);

    public async Task AddAsync(OrderNumberSequence sequence, CancellationToken ct = default)
        => await Ctx.OrderNumberSequences.AddAsync(sequence, ct);

    public Task DeleteAsync(OrderNumberSequence sequence, CancellationToken ct = default)
    {
        Ctx.OrderNumberSequences.Remove(sequence);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => Ctx.SaveChangesAsync(ct);

    public async Task<string?> AllocateAsync(Guid clientId, CancellationToken ct = default)
    {
        // A plain command rather than Database.SqlQuery<T>: EF may wrap a SqlQuery in a subquery,
        // and Postgres doesn't allow UPDATE ... RETURNING there. Unqualified table name — the
        // connection's search_path routes it to the tenant schema. The single-statement UPDATE is
        // what makes allocation safe under concurrency (row lock; no read-modify-write).
        var conn = Ctx.Database.GetDbConnection();
        var openedHere = conn.State != System.Data.ConnectionState.Open;
        if (openedHere) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE order_number_sequences
                SET next_value = next_value + 1, updated_at = now()
                WHERE client_id = @client_id
                RETURNING next_value - 1, prefix, suffix, width
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "client_id";
            p.Value = clientId;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return OrderNumberSequence.Format(
                prefix: reader.GetString(1), suffix: reader.GetString(2), width: reader.GetInt32(3), value: reader.GetInt64(0));
        }
        finally
        {
            if (openedHere) await conn.CloseAsync();
        }
    }

    public async Task<string?> AssignToCallRecordAsync(Guid callRecordId, string orderNumber, CancellationToken ct = default)
    {
        await Ctx.Database.ExecuteSqlAsync($"""
            UPDATE call_records SET order_number = {orderNumber}
            WHERE id = {callRecordId} AND order_number IS NULL
            """, ct);

        return await Ctx.CallRecords
            .Where(r => r.Id == callRecordId)
            .Select(r => r.OrderNumber)
            .FirstOrDefaultAsync(ct);
    }
}
