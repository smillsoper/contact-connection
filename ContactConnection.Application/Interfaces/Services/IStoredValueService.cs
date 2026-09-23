namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// A generic, free-form key/value store scoped to a tenant, client, or campaign — call-record-
/// centric like ICustomFieldService so node handlers in either flow engine stay simple: given the
/// current call, resolve which tenant/client/campaign "scope" the requested scope maps to.
/// Deliberately NOT typed/definition-driven (see ContactConnection.Domain.Entities.StoredValue).
/// </summary>
public interface IStoredValueService
{
    /// <summary>Null if nothing is stored for this key at this scope, or the stored value has expired.</summary>
    Task<string?> GetAsync(Guid callRecordId, string scope, string keyName, CancellationToken ct = default);

    /// <summary>Upserts — a second Set for the same (scope, keyName) replaces the value and expiry in place.</summary>
    Task SetAsync(Guid callRecordId, string scope, string keyName, string value, DateTimeOffset? expiresAt, CancellationToken ct = default);
}
