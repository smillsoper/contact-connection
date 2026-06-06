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
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private TenantApiEndpoint() { }

    public static TenantApiEndpoint Create(
        Guid definitionId,
        string apiSubType,
        string name,
        string path,
        string? httpMethod = null,
        string? description = null,
        int sortOrder = 0)
    {
        if (!Entities.ApiSubType.IsValid(apiSubType))
            throw new ArgumentException($"Unknown API sub-type '{apiSubType}'.", nameof(apiSubType));

        return new TenantApiEndpoint
        {
            Id = Guid.NewGuid(),
            DefinitionId = definitionId,
            ApiSubType = apiSubType,
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

    public void UpdateSubType(string apiSubType)
    {
        if (!Entities.ApiSubType.IsValid(apiSubType))
            throw new ArgumentException($"Unknown API sub-type '{apiSubType}'.", nameof(apiSubType));
        ApiSubType = apiSubType;
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
}
