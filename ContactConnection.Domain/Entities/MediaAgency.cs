namespace ContactConnection.Domain.Entities;

/// <summary>
/// A media agency a tenant's clients buy airtime through — e.g. Cannella (S171, Media Agency Phase A).
/// Kept as a managed list so assignments, reporting and the per-agency export (Cannella CORE, Phase B)
/// all use one consistent name. <see cref="Fields"/> are the agency's own per-number data points
/// (PRODUCTCODE, ACCESS CODE, MEDIA TYPE…) — defined once here so every assignment uses the exact same
/// names; CRMPro's free-form names drifted (ACCESS CODE vs ACCESSCODE) and broke exports.
/// </summary>
public class MediaAgency
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public List<MediaAgencyField> Fields { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private MediaAgency() { }

    public static MediaAgency Create(Guid tenantId, string name, IEnumerable<MediaAgencyField>? fields = null)
    {
        var now = DateTimeOffset.UtcNow;
        var agency = new MediaAgency { Id = Guid.NewGuid(), TenantId = tenantId, CreatedAt = now, UpdatedAt = now };
        agency.Update(name, fields ?? [], isActive: true);
        return agency;
    }

    public void Update(string name, IEnumerable<MediaAgencyField> fields, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Agency name is required.", nameof(name));
        Name = name.Trim();
        // Field names are the export's column keys — trimmed, unique case-insensitively, order kept.
        Fields = fields
            .Where(f => !string.IsNullOrWhiteSpace(f.Name))
            .Select(f => f with { Name = f.Name.Trim() })
            .DistinctBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>One data point an agency tracks per number, e.g. "PRODUCTCODE".</summary>
public record MediaAgencyField(string Name, bool Required = false);
