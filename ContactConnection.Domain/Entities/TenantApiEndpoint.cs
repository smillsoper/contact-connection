namespace ContactConnection.Domain.Entities;

public class TenantApiEndpoint
{
    public Guid Id { get; private set; }
    public Guid DefinitionId { get; private set; }
    public string ApiSubType { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Path { get; private set; } = string.Empty;
    public string? HttpMethod { get; private set; }
    public string? RequestBodyTemplate { get; private set; }
    public string QueryParams { get; private set; } = "{}";
    public string Headers { get; private set; } = "{}";
    public string ResponseMapping { get; private set; } = "{}";
    public int SortOrder { get; private set; }
    public bool IsPreferred { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// Opt-in override for the resilience engine's default retry policy. GET/HEAD/PUT/DELETE are
    /// always retry-safe by HTTP semantics regardless of this flag; POST/PATCH are NOT retried on
    /// an ambiguous failure (timeout, 5xx — we can't tell if the vendor already processed it) by
    /// default, since retrying a non-idempotent call risks a duplicate order/submission. Set this
    /// true only when the vendor itself guarantees idempotency for this specific endpoint (e.g.
    /// their own idempotency-key support) — see API_HARDENING_CHECKLIST.md Tier 1.
    /// </summary>
    public bool IsRetrySafe { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private TenantApiEndpoint() { }

    /// <summary>
    /// apiCategory is the parent definition's category — endpoints under a "general" category
    /// definition have no fixed sub-type vocabulary (ApiSubType is category-specific for
    /// address/order/fulfillment/media), so sub-type validation and storage are skipped for them.
    /// </summary>
    public static TenantApiEndpoint Create(
        Guid definitionId,
        string apiCategory,
        string apiSubType,
        string name,
        string path,
        string? httpMethod = null,
        string? description = null,
        int sortOrder = 0)
    {
        var isGeneral = apiCategory == Entities.ApiCategory.General;
        if (!isGeneral && !Entities.ApiSubType.IsValid(apiSubType))
            throw new ArgumentException($"Unknown API sub-type '{apiSubType}'.", nameof(apiSubType));

        return new TenantApiEndpoint
        {
            Id = Guid.NewGuid(),
            DefinitionId = definitionId,
            ApiSubType = isGeneral ? string.Empty : apiSubType,
            Name = name.Trim(),
            Description = description?.Trim(),
            Path = path.Trim(),
            HttpMethod = string.IsNullOrWhiteSpace(httpMethod) ? null : httpMethod.ToUpperInvariant(),
            SortOrder = sortOrder,
            IsPreferred = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, string path, string? httpMethod, string? description, int? sortOrder = null)
    {
        Name = name.Trim();
        Description = description?.Trim();
        Path = path.Trim();
        HttpMethod = string.IsNullOrWhiteSpace(httpMethod) ? null : httpMethod.ToUpperInvariant();
        if (sortOrder.HasValue) SortOrder = sortOrder.Value;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateSubType(string apiCategory, string apiSubType)
    {
        var isGeneral = apiCategory == Entities.ApiCategory.General;
        if (!isGeneral && !Entities.ApiSubType.IsValid(apiSubType))
            throw new ArgumentException($"Unknown API sub-type '{apiSubType}'.", nameof(apiSubType));
        ApiSubType = isGeneral ? string.Empty : apiSubType;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetPreferred() { IsPreferred = true; UpdatedAt = DateTimeOffset.UtcNow; }
    public void ClearPreferred() { IsPreferred = false; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetRequestBodyTemplate(string? template) { RequestBodyTemplate = template; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetQueryParams(string queryParamsJson) { QueryParams = queryParamsJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetHeaders(string headersJson) { Headers = headersJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetResponseMapping(string mappingJson) { ResponseMapping = mappingJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Activate() { IsActive = true; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetRetrySafe(bool isRetrySafe) { IsRetrySafe = isRetrySafe; UpdatedAt = DateTimeOffset.UtcNow; }
}
