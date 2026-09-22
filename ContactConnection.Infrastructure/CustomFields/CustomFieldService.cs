using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.CustomFields;
using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.CustomFields;

public class CustomFieldService : ICustomFieldService
{
    private readonly ICustomFieldDefinitionRepository _definitions;
    private readonly ICustomFieldValueRepository _values;
    private readonly ICallRecordRepository _callRecords;

    public CustomFieldService(
        ICustomFieldDefinitionRepository definitions,
        ICustomFieldValueRepository values,
        ICallRecordRepository callRecords)
    {
        _definitions = definitions;
        _values = values;
        _callRecords = callRecords;
    }

    public async Task<List<ResolvedCustomField>> GetFieldsForCallAsync(Guid callRecordId, CancellationToken ct = default)
    {
        var record = await _callRecords.GetByIdAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found");

        var defs = await _definitions.GetForContextAsync(record.TenantId, record.ClientId, record.CampaignId, ct);
        var resolved = ResolveMostSpecific(defs);

        var values = await _values.GetByCallRecordAsync(callRecordId, ct);
        var valueMap = values.ToDictionary(v => v.DefinitionId);

        return resolved
            .Select(d => new ResolvedCustomField(d, valueMap.GetValueOrDefault(d.Id)))
            .ToList();
    }

    public async Task SetValueAsync(Guid callRecordId, Guid definitionId, string rawValue, CancellationToken ct = default)
    {
        var record = await _callRecords.GetByIdAsync(callRecordId, ct)
            ?? throw new InvalidOperationException($"Call record {callRecordId} not found");

        var def = await _definitions.GetByIdAsync(definitionId, ct)
            ?? throw new InvalidOperationException($"Custom field definition {definitionId} not found");

        // Mirror GetForContextAsync's scope predicate — a definition that wouldn't be returned for
        // this call's tenant/client/campaign must not be writable either, or the value becomes
        // permanently unreadable through the only read path (GetFieldsForCallAsync).
        if (!IsInScope(def, record))
            throw new InvalidOperationException(
                $"Custom field definition {definitionId} is not in scope for call record {callRecordId}");

        var existing = await _values.GetByCallRecordAndDefinitionAsync(callRecordId, definitionId, ct);
        var cfv = existing ?? CustomFieldValue.Create(callRecordId, definitionId);

        ApplyTypedValue(cfv, def.DataTypeName, rawValue);

        if (existing == null)
            await _values.AddAsync(cfv, ct);

        await _values.SaveChangesAsync(ct);
        await RefreshSnapshotAsync(callRecordId, ct);
    }

    public async Task DeleteValueAsync(Guid callRecordId, Guid definitionId, CancellationToken ct = default)
    {
        await _values.DeleteAsync(callRecordId, definitionId, ct);
        await _values.SaveChangesAsync(ct);
        await RefreshSnapshotAsync(callRecordId, ct);
    }

    // Scope resolution: for each field_name keep the most specific definition (campaign > client > tenant)
    private static List<CustomFieldDefinition> ResolveMostSpecific(List<CustomFieldDefinition> all)
        => all
            .GroupBy(d => d.FieldName)
            .Select(g => g.OrderBy(d => d.ScopeRank).ThenBy(d => d.DisplayOrder).First())
            .OrderBy(d => d.DisplayOrder)
            .ToList();

    private static bool IsInScope(CustomFieldDefinition def, CallRecord record)
        => def.IsActive &&
           def.TenantId == record.TenantId &&
           (def.ClientId == null || def.ClientId == record.ClientId) &&
           (def.CampaignId == null || def.CampaignId == record.CampaignId);

    private static void ApplyTypedValue(CustomFieldValue cfv, string dataTypeName, string raw)
    {
        switch (dataTypeName)
        {
            case CustomFieldDataType.String:
                cfv.SetString(raw);
                break;
            case CustomFieldDataType.Integer:
                cfv.SetInteger(long.Parse(raw));
                break;
            case CustomFieldDataType.Decimal:
            case CustomFieldDataType.Currency:
                cfv.SetDecimal(decimal.Parse(raw));
                break;
            case CustomFieldDataType.Boolean:
                cfv.SetBoolean(bool.Parse(raw));
                break;
            case CustomFieldDataType.Date:
                cfv.SetDate(DateOnly.Parse(raw));
                break;
            case CustomFieldDataType.DateTime:
                cfv.SetDatetime(DateTimeOffset.Parse(raw));
                break;
            case CustomFieldDataType.Json:
                cfv.SetJson(raw);
                break;
            default:
                throw new ArgumentException($"Unsupported data type: {dataTypeName}");
        }
    }

    private async Task RefreshSnapshotAsync(Guid callRecordId, CancellationToken ct)
    {
        var currentValues = await _values.GetByCallRecordAsync(callRecordId, ct);

        var snapshot = new Dictionary<string, object?>();
        foreach (var v in currentValues.Where(v => v.Definition != null))
            snapshot[v.Definition!.FieldName] = v.GetTypedValue();

        var record = await _callRecords.GetByIdAsync(callRecordId, ct);
        record?.UpdateCustomFieldsSnapshot(JsonSerializer.Serialize(snapshot));
        await _callRecords.SaveChangesAsync(ct);
    }
}
