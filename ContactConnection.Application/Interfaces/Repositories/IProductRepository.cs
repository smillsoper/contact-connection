using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default);

    /// <summary>
    /// Full-text search with optional category + attribute-value facets.
    /// Returns searchable, non-reporting-only products ordered by description, unless
    /// <paramref name="includeAll"/> is set (the admin product list needs to see a product even
    /// after it's been toggled non-searchable, or it becomes invisible to manage).
    /// Multiple attributeValueIds are ANDed — product must have ALL specified values assigned.
    /// </summary>
    Task<List<Product>> SearchAsync(
        string? query,
        Guid? categoryId,
        IReadOnlyList<Guid>? attributeValueIds,
        int page, int pageSize,
        bool includeAll = false,
        CancellationToken ct = default,
        ProductScopeFilter? scope = null);

    /// <summary>
    /// Returns all products sharing a MixMatchCode (used by PricingService during cart total calculation).
    /// </summary>
    Task<List<Product>> GetByMixMatchCodeAsync(string mixMatchCode, CancellationToken ct = default);

    Task AddAsync(Product product, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Client/campaign narrowing for product search (S169).
/// <list type="bullet">
/// <item><see cref="ForCall"/> = false (admin list): <see cref="TenantWideOnly"/> shows only unscoped
/// products; otherwise ClientId must match and, with a CampaignId, the product must be available in that
/// campaign (no campaign list, or the campaign listed).</item>
/// <item><see cref="ForCall"/> = true (agent cart): products that fit a call on ClientId/CampaignId —
/// tenant-wide or scoped to that client/campaign — and that have at least one active offer fitting the
/// call, so the agent never picks a product with nothing they can sell.</item>
/// </list>
/// </summary>
public record ProductScopeFilter(Guid? ClientId, Guid? CampaignId, bool ForCall = false, bool TenantWideOnly = false);

