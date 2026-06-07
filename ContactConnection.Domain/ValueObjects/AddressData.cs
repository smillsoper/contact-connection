namespace ContactConnection.Domain.ValueObjects;

/// <summary>
/// Represents a physical mailing address. Modeled from production usage in CRMPro.
/// Stored as JSONB on call_records.addresses (billing/shipping).
/// </summary>
public class AddressData
{
    public string? Prefix { get; set; }       // Street direction prefix: N, SW, etc.
    public string? Street { get; set; }
    public string? UnitPrefix { get; set; }   // Apt, Ste, Unit, etc.
    public string? Unit { get; set; }
    public string? Company { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleInitial { get; set; }
    public string? LastName { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Zip4 { get; set; }
    public string? Country { get; set; }

    // Address classification flags
    public bool IsPOBox { get; set; }
    public bool IsCanada { get; set; }
    public bool IsForeign { get; set; }
    public bool IsMilitary { get; set; }
    public bool IsOutlyingUS { get; set; }    // Guam, PR, USVI, etc.
    public bool IsAKHI { get; set; }          // Alaska or Hawaii

    // Geo coordinates — populated by ZIP code lookup
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Verification state
    public bool IsVerified { get; set; }
    public string? VerificationSource { get; set; }   // usps, smartystreets, manual
}
