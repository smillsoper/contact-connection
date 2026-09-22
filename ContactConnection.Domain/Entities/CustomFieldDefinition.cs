using ContactConnection.Domain.CustomFields;

namespace ContactConnection.Domain.Entities;

/// <summary>
/// Defines a custom field scoped to tenant, client, or campaign.
/// Scope resolution: campaign > client > tenant — most specific wins.
/// See ARCHITECTURE.md §20.
/// </summary>
public class CustomFieldDefinition
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? ClientId { get; private set; }    // null = tenant-wide
    public Guid? CampaignId { get; private set; }  // null = client-wide or tenant-wide

    public string FieldName { get; private set; } = "";      // machine key, stable
    public string DisplayLabel { get; private set; } = "";   // UI label, editable
    public string DataTypeName { get; private set; } = "";   // FK to data_types.type_name

    public bool IsRequired { get; private set; }
    public string? ValidationRules { get; private set; }     // JSONB: min/max/pattern/options
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    private CustomFieldDefinition() { }

    public static CustomFieldDefinition Create(
        Guid tenantId,
        string fieldName,
        string displayLabel,
        string dataTypeName,
        bool isRequired = false,
        int displayOrder = 0,
        Guid? clientId = null,
        Guid? campaignId = null,
        string? validationRules = null)
    {
        if (!CustomFieldDataType.All.Contains(dataTypeName))
            throw new ArgumentException($"Unknown data type: {dataTypeName}", nameof(dataTypeName));

        if (campaignId.HasValue && !clientId.HasValue)
            throw new ArgumentException("A campaign-scoped field must also specify its client.", nameof(clientId));

        return new CustomFieldDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            CampaignId = campaignId,
            FieldName = fieldName.Trim().ToLowerInvariant(),
            DisplayLabel = displayLabel,
            DataTypeName = dataTypeName,
            IsRequired = isRequired,
            ValidationRules = validationRules,
            DisplayOrder = displayOrder,
            IsActive = true
        };
    }

    public void UpdateLabel(string displayLabel) => DisplayLabel = displayLabel;
    public void SetDisplayOrder(int order) => DisplayOrder = order;
    public void SetRequired(bool required) => IsRequired = required;
    public void SetValidationRules(string? rules) => ValidationRules = rules;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>Specificity rank for scope resolution — lower = more specific.</summary>
    public int ScopeRank =>
        CampaignId.HasValue ? 0 :
        ClientId.HasValue ? 1 : 2;
}
