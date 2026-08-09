namespace ContactConnection.Api.Endpoints;

// Shared snapshot shapes for API Definition/Endpoint version history — TenantApiDefinition and
// PortalApiDefinition (same for the Endpoint pair) are structurally identical classes, so one
// snapshot record covers both; only the tiny Build/Apply glue in each endpoints file differs
// because the entity types themselves differ.

public record ApiDefinitionSnapshot(
    string ApiCategory, string? Provider, string Name, string? Description, string HttpMethod,
    string BaseUrl, int TimeoutSeconds, string Headers, string QueryParams,
    string? RequestBodyTemplate, string ResponseMapping, string AuthConfig, bool IsActive);

public record ApiEndpointSnapshot(
    string ApiSubType, string Name, string? Description, string Path, string? HttpMethod,
    string? RequestBodyTemplate, string QueryParams, string Headers, string ResponseMapping,
    int SortOrder, bool IsPreferred, bool IsActive, bool IsRetrySafe);
