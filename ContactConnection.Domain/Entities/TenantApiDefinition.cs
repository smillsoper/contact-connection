namespace ContactConnection.Domain.Entities;

public class TenantApiDefinition
{
    public Guid Id { get; private set; }
    public string ApiCategory { get; private set; } = string.Empty;
    public string? Provider { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string HttpMethod { get; private set; } = "POST";
    public string BaseUrl { get; private set; } = string.Empty;
    public int TimeoutSeconds { get; private set; } = 30;
    public string Headers { get; private set; } = "{}";
    public string QueryParams { get; private set; } = "{}";
    public string? RequestBodyTemplate { get; private set; }
    public string ResponseMapping { get; private set; } = "{}";
    public string AuthConfig { get; private set; } = "{\"type\":\"none\"}";
    public bool IsActive { get; private set; }
    /// <summary>Outbound calls per minute allowed against this definition, enforced per
    /// definitionId (shared across every tenant using this row — the point for a Portal
    /// definition backed by a platform-default credential). Null = unlimited (opt-in, matching
    /// IsRetrySafe's default-off pattern — an existing definition's behavior never changes until
    /// someone sets this). See API_HARDENING_CHECKLIST.md Tier 2.</summary>
    public int? RateLimitPerMinute { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private TenantApiDefinition() { }

    public static TenantApiDefinition Create(
        string apiCategory,
        string name,
        string httpMethod,
        string baseUrl,
        string? description = null,
        string? provider = null,
        int timeoutSeconds = 30)
    {
        if (!Entities.ApiCategory.IsValid(apiCategory))
            throw new ArgumentException($"Unknown API category '{apiCategory}'.", nameof(apiCategory));

        return new TenantApiDefinition
        {
            Id = Guid.NewGuid(),
            ApiCategory = apiCategory,
            Provider = provider?.Trim(),
            Name = name.Trim(),
            Description = description?.Trim(),
            HttpMethod = httpMethod.ToUpperInvariant(),
            BaseUrl = baseUrl.Trim(),
            TimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 30,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, string httpMethod, string baseUrl, string? description, string? provider = null, int? timeoutSeconds = null)
    {
        Name = name.Trim();
        Description = description?.Trim();
        HttpMethod = httpMethod.ToUpperInvariant();
        BaseUrl = baseUrl.Trim();
        if (provider is not null) Provider = provider.Trim() == string.Empty ? null : provider.Trim();
        if (timeoutSeconds.HasValue && timeoutSeconds.Value > 0) TimeoutSeconds = timeoutSeconds.Value;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateCategory(string apiCategory)
    {
        if (!Entities.ApiCategory.IsValid(apiCategory))
            throw new ArgumentException($"Unknown API category '{apiCategory}'.", nameof(apiCategory));
        ApiCategory = apiCategory;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetHeaders(string headersJson) { Headers = headersJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetQueryParams(string queryParamsJson) { QueryParams = queryParamsJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetRequestBodyTemplate(string? template) { RequestBodyTemplate = template; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetResponseMapping(string mappingJson) { ResponseMapping = mappingJson; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetAuthConfig(string authConfigJson) { AuthConfig = authConfigJson; UpdatedAt = DateTimeOffset.UtcNow; }

    public void SetRateLimit(int? perMinute)
    {
        if (perMinute is <= 0) throw new ArgumentException("Rate limit must be null (unlimited) or a positive number of requests per minute.", nameof(perMinute));
        RateLimitPerMinute = perMinute;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate() { IsActive = true; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTimeOffset.UtcNow; }
}
