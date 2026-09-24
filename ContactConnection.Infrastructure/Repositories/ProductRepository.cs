using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private TenantDbContext? _ctx;
    private readonly ScopedTenantDbContextFactory _factory;

    public ProductRepository(ScopedTenantDbContextFactory factory)
        => _factory = factory;

    private TenantDbContext Ctx => _ctx ??= _factory.Create();

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Ctx.Products
            .Include(p => p.Kits)
            .Include(p => p.Offers)
            .Include(p => p.Categories)
            .Include(p => p.AttributeValues)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default)
        => Ctx.Products
            .Include(p => p.Kits)
            .Include(p => p.Offers)
            .Include(p => p.Categories)
            .Include(p => p.AttributeValues)
            .FirstOrDefaultAsync(p => p.Sku == sku, ct);

    public async Task<List<Product>> SearchAsync(
        string? query,
        Guid? categoryId,
        IReadOnlyList<Guid>? attributeValueIds,
        int page, int pageSize,
        CancellationToken ct = default)
    {
        var q = Ctx.Products
            .Include(p => p.Categories)
            .Include(p => p.AttributeValues)
            .Where(p => p.Searchable && !p.ReportingOnly);

        // Keywords/AliasSKUs are stored as jsonb via a value converter (opaque scalar to EF/SQL
        // translation), so they can't be matched here without a raw-SQL fragment — left as a
        // documented gap rather than risking an untranslatable-query exception. Description and
        // Sku are plain scalar columns and match cleanly.
        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(p =>
                EF.Functions.ILike(p.Description, $"%{query}%") ||
                EF.Functions.ILike(p.Sku, $"%{query}%"));

        if (categoryId.HasValue)
            q = q.Where(p => p.Categories.Any(c => c.Id == categoryId.Value));

        if (attributeValueIds?.Count > 0)
            foreach (var valueId in attributeValueIds)
                q = q.Where(p => p.AttributeValues.Any(v => v.Id == valueId));

        return await q
            .OrderBy(p => p.Description)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns products that have at least one active offer with the given MixMatchCode.
    /// Used by PricingService to group items for cross-item price break evaluation.
    /// </summary>
    public Task<List<Product>> GetByMixMatchCodeAsync(string mixMatchCode, CancellationToken ct = default)
        => Ctx.Products
            .Where(p => p.Offers.Any(o => o.MixMatchCode == mixMatchCode && o.IsActive))
            .ToListAsync(ct);

    public async Task AddAsync(Product product, CancellationToken ct = default)
        => await Ctx.Products.AddAsync(product, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => Ctx.SaveChangesAsync(ct);
}
