using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Services;

public record ResolvedCustomField(CustomFieldDefinition Definition, CustomFieldValue? Value);

public interface ICustomFieldService
{
    /// <summary>
    /// Returns the scope-resolved set of custom field definitions for a call record,
    /// each paired with its current value (null if not yet set).
    /// Campaign > client > tenant — most specific definition wins per field name.
    /// </summary>
    Task<List<ResolvedCustomField>> GetFieldsForCallAsync(Guid callRecordId, CancellationToken ct = default);

    /// <summary>Upserts a typed value, then refreshes the call_records.custom_fields snapshot.</summary>
    Task SetValueAsync(Guid callRecordId, Guid definitionId, string rawValue, CancellationToken ct = default);

    /// <summary>
    /// A CRM script's write (set_custom_field). Normally the same as <see cref="SetValueAsync"/>. On an interaction
    /// working a different campaign than the call record (a mid-call transfer, sales → CS, S178) the value goes into
    /// that interaction's own custom fields instead, so the record keeps the first campaign's values (what
    /// commissions, exports and media reports read). Same type validation; scope is checked against the
    /// interaction's campaign.
    /// </summary>
    Task SetValueFromScriptAsync(Guid callRecordId, Guid interactionId, Guid definitionId, string rawValue, CancellationToken ct = default);

    /// <summary>Removes a value and refreshes the snapshot.</summary>
    Task DeleteValueAsync(Guid callRecordId, Guid definitionId, CancellationToken ct = default);
}
